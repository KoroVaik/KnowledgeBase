import { apiFetch, readErrorMessage } from './http'

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

/** Asked for on click, not at render: the answer is a signed URL that expires in minutes. */
export async function fetchDownloadUrl(storedFileName: string): Promise<string> {
  const response = await apiFetch(`${assetPath(storedFileName)}/link`)

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not get the download link'))
  }

  return ((await response.json()) as AssetLink).url
}

// Three steps: only the API may sign, only the bucket takes bytes, only the API writes the
// row that makes them visible. Nothing shows in the listing until confirm succeeds.
export async function uploadAsset(
  file: File,
  onProgress?: (fraction: number) => void,
): Promise<UploadedAsset> {
  const link = await requestUploadLink(file)
  await putToBucket(link, file, onProgress)

  return confirmUpload(link, file)
}

async function requestUploadLink(file: File): Promise<UploadLink> {
  const response = await apiFetch('/api/assets/upload-link', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      fileName: file.name,
      contentType: file.type,
      sizeBytes: file.size,
    }),
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Upload failed'))
  }

  return (await response.json()) as UploadLink
}

// XMLHttpRequest, not fetch: only `xhr.upload` reports request-body progress.
function putToBucket(
  link: UploadLink,
  file: File,
  onProgress?: (fraction: number) => void,
): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const request = new XMLHttpRequest()

    request.open('PUT', link.url)
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

async function confirmUpload(link: UploadLink, file: File): Promise<UploadedAsset> {
  const response = await apiFetch(`${assetPath(link.fileName)}/confirm`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ originalFileName: file.name, contentType: link.contentType }),
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'The file was stored but could not be registered'))
  }

  return (await response.json()) as UploadedAsset
}

export async function fetchAssets(): Promise<AssetSummary[]> {
  const response = await apiFetch('/api/assets')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the files'))
  }

  return (await response.json()) as AssetSummary[]
}

export async function deleteAsset(storedFileName: string): Promise<void> {
  const response = await apiFetch(assetPath(storedFileName), { method: 'DELETE' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Delete failed'))
  }
}

/** Re-queues a file the worker failed on, or whose note was deleted while the file was kept. */
export async function processAsset(storedFileName: string): Promise<void> {
  const response = await apiFetch(`${assetPath(storedFileName)}/process`, { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not queue the file'))
  }
}
