import { readErrorMessage } from './http'

/** Mirrors UploadedAssetResponse in backend/Controllers/Assets (ASP.NET serialises camelCase). */
export interface UploadedAsset {
  id: string
  storedFileName: string
  originalFileName: string
  contentType: string
  sizeBytes: number
  uploadedAtUtc: string
}

export async function uploadAsset(file: File): Promise<UploadedAsset> {
  const formData = new FormData()
  // The field name must stay "file" - it binds to the IFormFile parameter name.
  formData.append('file', file)

  const response = await fetch('/api/assets', {
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
