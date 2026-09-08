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

/**
 * Plain <a href> target rather than a fetch: the API shares the origin, so the browser
 * attaches the session cookie and offers the file itself.
 */
export function assetDownloadUrl(storedFileName: string): string {
  return `/api/assets/${encodeURIComponent(storedFileName)}`
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
