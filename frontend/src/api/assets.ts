import { apiFetch, readErrorMessage } from './http'

/** Mirrors UploadedAssetResponse in backend/Controllers/Assets (ASP.NET serialises camelCase). */
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
  sizeBytes: number
  uploadedAtUtc: string
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

/** The API-side resource for one stored file: base for /link, /confirm and DELETE. */
function assetPath(storedFileName: string): string {
  return `/api/assets/${encodeURIComponent(storedFileName)}`
}

/**
 * Where the bytes actually are. Asked for on click rather than when the row renders: the
 * answer is a signed URL that works for anyone holding it, and it expires in minutes.
 */
export async function fetchDownloadUrl(storedFileName: string): Promise<string> {
  const response = await apiFetch(`${assetPath(storedFileName)}/link`)

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not get the download link'))
  }

  return ((await response.json()) as AssetLink).url
}

/**
 * Three steps, because no single party can do the job: only the API may sign, only the
 * bucket takes the bytes, and only the API can write the row that makes them visible.
 * Nothing exists in the listing until the last step succeeds. The bytes never touch the API.
 */
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

/**
 * XMLHttpRequest rather than fetch for one reason: only `xhr.upload` reports how much of
 * the request body has left the browser. fetch hands the body over and says nothing until
 * the response comes back, so a progress bar over a fetch PUT is not possible.
 */
function putToBucket(
  link: UploadLink,
  file: File,
  onProgress?: (fraction: number) => void,
): Promise<void> {
  return new Promise<void>((resolve, reject) => {
    const request = new XMLHttpRequest()

    request.open('PUT', link.url)
    // The only header we may send. It is signed into the URL, so it has to match exactly;
    // any extra header widens the CORS preflight the bucket has no rule for.
    request.setRequestHeader('Content-Type', link.contentType)

    request.upload.addEventListener('progress', (event) => {
      if (event.lengthComputable) {
        onProgress?.(event.loaded / event.total)
      }
    })

    // A cross-origin PUT the bucket has no CORS rule for fails before any response exists,
    // and looks identical to the network being down - status stays 0 either way.
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
