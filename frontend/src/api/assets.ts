import { batchRequest } from './batch'
import { clearImages } from '../components/ProgressiveImage/imageCache'
import { apiFetch, clearResourceReads, readErrorMessage } from './http'
import { coalescedRead } from './coalescedRead'
import { captureRequestContext, diagnosticId, record, requestContext, startRequest } from '../diagnostics/diagnostics'
import type { RequestContext } from '../diagnostics/diagnostics'

/** Mirrors UploadedAssetResponse in backend Controllers/Assets. */
export interface UploadedAsset {
  id: string
  storedFileName: string
  originalFileName: string
  contentType: string
  sizeBytes: number
  uploadedAtUtc: string
}

/** Mirrors AssetSummaryResponse in backend/Controllers/Assets. */
export interface AssetSummary {
  id: string
  storedFileName: string
  originalFileName: string
  /** Stored MIME type - lets the UI tell an image (previewable inline) from the rest. */
  contentType: string
  sizeBytes: number
  uploadedAtUtc: string
  /** 'Pending' | 'Running' | 'Done' | 'Failed' | 'Skipped', or null if not a pipeline type. */
  processingStatus: string | null
  /** Error text for 'Failed' / 'Skipped'. */
  processingError: string | null
  noteId: string | null
  /** False with no note yet, or a note with no tag. */
  hasTags: boolean
  /** EXIF, filled in once the pipeline has run - null before that, and null after if the
   *  file carried none (a screenshot, a re-encoded photo). */
  capturedAtUtc: string | null
  latitude: number | null
  longitude: number | null
}

/** Mirrors AssetLinkResponse in backend/Controllers/Assets. */
interface AssetLink {
  url: string
  expiresAtUtc: string
}

/** Mirrors UploadLinkResponse in backend/Controllers/Assets. */
interface UploadLink {
  fileName: string
  url: string
  contentType: string
  expiresAtUtc: string
}

function assetPath(storedFileName: string): string {
  return `/api/assets/${encodeURIComponent(storedFileName)}`
}

const downloadBatch = batchRequest<{ fileName: string }, AssetLink>('/api/assets/download-links')
const uploadBatch = batchRequest<{ fileName: string; contentType: string; sizeBytes: number }, UploadLink>('/api/assets/upload-links')
const confirmBatch = batchRequest<{ fileName: string; originalFileName: string; contentType: string }, UploadedAsset>('/api/assets/confirm-batch')
const links = new Map<string, AssetLink>()
const pendingLinks = new Map<string, Promise<string>>()
let cacheGeneration = 0

export function clearAssetCaches(fileName?: string) {
  if (fileName === undefined) {
    cacheGeneration++
    links.clear(); pendingLinks.clear()
    downloadBatch.clear(); uploadBatch.clear(); confirmBatch.clear()
    clearResourceReads()
    assetReads.clear()
    previousAssets = []
    attempts = new WeakMap()
  } else {
    links.delete(fileName)
    pendingLinks.delete(fileName)
    previousAssets = previousAssets.filter(asset => asset.storedFileName !== fileName)
  }
  clearImages(fileName)
}

export function fetchDownloadUrl(storedFileName: string, context?: RequestContext, force = false): Promise<string> {
  const pending = pendingLinks.get(storedFileName)
  if (pending) return pending
  const cached = links.get(storedFileName)
  if (!force && cached && Date.parse(cached.expiresAtUtc) > Date.now() + 30_000) {
    links.delete(storedFileName); links.set(storedFileName, cached)
    return Promise.resolve(cached.url)
  }
  const generation = cacheGeneration
  const promise = downloadBatch.request({ fileName: storedFileName }, captureRequestContext(context)).then(link => {
    if (generation !== cacheGeneration || pendingLinks.get(storedFileName) !== promise) throw new Error('Preview invalidated')
    links.delete(storedFileName)
    links.set(storedFileName, link)
    while (links.size > 512) links.delete(links.keys().next().value!)
    return link.url
  }).finally(() => { if (pendingLinks.get(storedFileName) === promise) pendingLinks.delete(storedFileName) })
  pendingLinks.set(storedFileName, promise)
  return promise
}

/** 'transfer' reports bucket-PUT progress; 'finalize' fires once, when only the confirm
 *  round-trip (a HEAD to the bucket + a Postgres write, constant cost) is still pending. */
export type UploadPhase = 'transfer' | 'finalize'

type UploadAttempt = { uploadId: string; link?: UploadLink; transferred: boolean }
let attempts = new WeakMap<File, UploadAttempt>()
let transferring = 0
const transferWaiters: (() => void)[] = []

async function withTransferSlot(action: () => Promise<void>) {
  if (transferring >= 4) await new Promise<void>(resolve => transferWaiters.push(resolve))
  else transferring++
  try { await action() } finally {
    const next = transferWaiters.shift()
    if (next) next()
    else transferring--
  }
}

// Three steps: only the API may sign, only the bucket takes bytes, only the API writes the
// row that makes them visible. Nothing shows in the listing until confirm succeeds.
export async function uploadAsset(
  file: File,
  onProgress?: (phase: UploadPhase, fraction: number) => void,
  context: RequestContext = {},
): Promise<UploadedAsset> {
  const attempt = attempts.get(file) ?? { uploadId: diagnosticId(), transferred: false }
  attempts.set(file, attempt)
  const generation = cacheGeneration
  const ensureSession = () => { if (generation !== cacheGeneration) throw new Error('Session changed') }
  context = { ...requestContext('UploadQueue', 'upload'), ...context, uploadId: attempt.uploadId, traceId: diagnosticId() }
  record('upload.started', { ...context, bytes: file.size })
  try {
    if (!attempt.link || !attempt.transferred && Date.parse(attempt.link.expiresAtUtc) <= Date.now() + 30_000) {
      attempt.link = await requestUploadLink(file, context)
    }
    ensureSession()
    const link = attempt.link
    context = { ...context, assetId: link.fileName.split('.')[0] }
    record('upload.link-created', context)
    if (!attempt.transferred) await withTransferSlot(async () => {
      ensureSession()
      // A large queue can outlive a signed PUT while waiting for its transfer slot.
      if (Date.parse(attempt.link!.expiresAtUtc) <= Date.now() + 30_000) attempt.link = await requestUploadLink(file, context)
      ensureSession()
      context = { ...context, assetId: attempt.link!.fileName.split('.')[0] }
      await putToBucket(attempt.link!, file, context, (fraction) => onProgress?.('transfer', fraction))
      attempt.transferred = true
    })
    ensureSession()
    onProgress?.('finalize', 0)
    record('upload.confirming', context)
    const asset = await confirmUpload(attempt.link!, file, context)
    ensureSession()
    attempts.delete(file)
    record('upload.completed', { ...context, assetId: asset.id })
    return asset
  } catch (error) {
    record('upload.failed', { ...context, errorType: error instanceof Error ? error.name : 'Unknown' }, 'error')
    throw error
  }
}

function requestUploadLink(file: File, context: RequestContext): Promise<UploadLink> {
  return uploadBatch.request({ fileName: file.name, contentType: file.type, sizeBytes: file.size }, context)
}

// XMLHttpRequest, not fetch: only `xhr.upload` reports request-body progress.
function putToBucket(
  link: UploadLink,
  file: File,
  context: RequestContext,
  onProgress?: (fraction: number) => void,
): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const request = new XMLHttpRequest()
    const diagnostic = startRequest('PUT', 'bucket/object', context)
    request.addEventListener('loadend', () => diagnostic.finish(request.status, request.status === 0 ? 'network-error' : 'completed', file.size))

    request.open('PUT', link.url)
    request.timeout = 300_000
    request.addEventListener('timeout', () => reject(new Error('The upload timed out')))
    request.addEventListener('abort', () => reject(new Error('The upload was cancelled')))
    // The only header allowed: it is in the signature, and any extra widens the CORS preflight.
    request.setRequestHeader('Content-Type', link.contentType)

    request.upload.addEventListener('progress', (event) => {
      if (event.lengthComputable) {
        onProgress?.(event.loaded / event.total)
      }
    })

    // A blocked cross-origin PUT and a dead network both leave status 0.
    request.addEventListener('error', () => {
      reject(unreachableBucketError())
    })

    request.addEventListener('load', () => {
      if (request.status === 0) {
        reject(unreachableBucketError())
        return
      }

      if (request.status < 200 || request.status > 299) {
        reject(new Error(`The storage bucket refused the file (HTTP ${request.status})`))
        return
      }

      resolve()
    })

    request.send(file)
  })
}

function unreachableBucketError(): Error {
  return new Error('Could not reach the storage bucket - it may be offline or refusing this origin')
}

function confirmUpload(link: UploadLink, file: File, context: RequestContext): Promise<UploadedAsset> {
  return confirmBatch.request({ fileName: link.fileName, originalFileName: file.name, contentType: link.contentType }, context)
}

let previousAssets: AssetSummary[] = []
const assetReads = coalescedRead<AssetSummary[]>()
export function fetchAssets(): Promise<AssetSummary[]> {
  const context = captureRequestContext()
  const generation = cacheGeneration
  return assetReads.read(async () => {
    const response = await apiFetch('/api/assets', undefined, context)
    if (!response.ok) throw new Error(await readErrorMessage(response, 'Could not load the files'))
    const assets = await response.json() as AssetSummary[]
    if (generation !== cacheGeneration) throw new Error('Session changed')
    const previous = new Map(previousAssets.map(asset => [asset.id, asset]))
    previousAssets = assets.map(asset => {
      const old = previous.get(asset.id)
      return old && (Object.keys(asset) as (keyof AssetSummary)[]).every(key => old[key] === asset[key]) ? old : asset
    })
    return previousAssets
  }, context.trigger !== 'poll' && context.trigger !== 'mount')
}

export async function deleteAsset(storedFileName: string): Promise<void> {
  const response = await apiFetch(assetPath(storedFileName), { method: 'DELETE' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Delete failed'))
  }
  clearAssetCaches(storedFileName)
}

/** Re-queues a file the worker failed on, or whose note was deleted while the file was kept. */
export async function processAsset(storedFileName: string): Promise<void> {
  const response = await apiFetch(`${assetPath(storedFileName)}/process`, { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not queue the file'))
  }
}
