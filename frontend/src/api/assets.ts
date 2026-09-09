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

/**
 * Plain <a href> target rather than a fetch: the API shares the origin, so the browser
 * attaches the session cookie and offers the file itself.
 */
export function assetDownloadUrl(storedFileName: string): string {
  return `/api/assets/${encodeURIComponent(storedFileName)}`
}

/**
 * Where the bytes actually are. Asked for on click rather than when the row renders: the
 * answer is a signed URL that works for anyone holding it, and it expires in minutes.
 */
export async function fetchDownloadUrl(storedFileName: string): Promise<string> {
  const response = await apiFetch(`${assetDownloadUrl(storedFileName)}/link`)

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not get the download link'))
  }

  return ((await response.json()) as AssetLink).url
}

export async function uploadAsset(file: File): Promise<UploadedAsset> {
  const formData = new FormData()
  // The field name must stay "file" - it binds to the IFormFile parameter name.
  formData.append('file', file)

  const response = await apiFetch('/api/assets', {
    method: 'POST',
    // No Content-Type header on purpose: the browser has to set it itself so the
    // multipart boundary is included.
    body: formData,
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Upload failed'))
  }

  return (await response.json()) as UploadedAsset
}

/**
 * Three steps, because no single party can do the job: only the API may sign, only the
 * bucket takes the bytes, and only the API can write the row that makes them visible.
 * Nothing exists in the listing until the last step succeeds.
 */
export async function uploadAssetToBucket(file: File): Promise<UploadedAsset> {
  const link = await requestUploadLink(file)
  await putToBucket(link, file)

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

async function putToBucket(link: UploadLink, file: File): Promise<void> {
  let response: Response

  try {
    response = await fetch(link.url, {
      method: 'PUT',
      // Signed into the URL, so it has to match exactly - the server decided the value,
      // the browser only repeats it.
      headers: { 'Content-Type': link.contentType },
      body: file,
    })
  } catch (cause) {
    // A cross-origin PUT the bucket has no CORS rule for fails before any response exists,
    // and looks identical to the network being down. Naming the likely cause here beats the
    // browser's "Failed to fetch".
    throw new Error('Could not reach the storage bucket - it may be offline or refusing this origin', {
      cause,
    })
  }

  if (!response.ok) {
    throw new Error(`The storage bucket refused the file (HTTP ${response.status})`)
  }
}

async function confirmUpload(link: UploadLink, file: File): Promise<UploadedAsset> {
  const response = await apiFetch(`${assetDownloadUrl(link.fileName)}/confirm`, {
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
  const response = await apiFetch(assetDownloadUrl(storedFileName), { method: 'DELETE' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Delete failed'))
  }
}
