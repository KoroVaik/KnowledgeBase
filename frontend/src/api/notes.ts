// Not a hardcoded "localhost": opened from a phone via the PC's LAN IP, "localhost"
// would resolve to the phone itself. Port matches the `http` launch profile.
const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? `http://${window.location.hostname}:5244`

/** Mirrors UploadedAssetResponse in backend/Program.cs (ASP.NET serialises camelCase). */
export interface UploadedAsset {
  id: string
  storedFileName: string
  originalFileName: string
  contentType: string
  sizeBytes: number
  uploadedAtUtc: string
}

export async function uploadNoteFile(file: File): Promise<UploadedAsset> {
  const formData = new FormData()
  // The field name must stay "file" - it binds to the IFormFile parameter name.
  formData.append('file', file)

  const response = await fetch(`${API_BASE_URL}/api/notes/upload`, {
    method: 'POST',
    // No Content-Type header on purpose: the browser has to set it itself so the
    // multipart boundary is included.
    body: formData,
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response))
  }

  return (await response.json()) as UploadedAsset
}

async function readErrorMessage(response: Response): Promise<string> {
  try {
    const body: unknown = await response.json()
    if (
      typeof body === 'object' &&
      body !== null &&
      'error' in body &&
      typeof body.error === 'string'
    ) {
      return body.error
    }
  } catch {
    // Body was not JSON (e.g. a plain 404 page).
  }

  return `Upload failed (HTTP ${response.status})`
}
