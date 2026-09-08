/**
 * The request never reached the API: the process is down, the machine is asleep, the network
 * is gone. Distinct from an error response, because the two need different answers - a 400 is
 * about what was sent, this is about the server not being there at all.
 */
export class ApiUnreachableError extends Error {
  constructor(cause: unknown) {
    // The browser's own wording ("Failed to fetch", "NetworkError when attempting to fetch
    // resource") differs per engine and means nothing to a reader.
    super('No connection to the server', { cause })
    this.name = 'ApiUnreachableError'
  }
}

/** `fetch` rejects only when there was no response at all, so every rejection is that case. */
export async function apiFetch(path: string, init?: RequestInit): Promise<Response> {
  try {
    return await fetch(path, init)
  } catch (cause) {
    throw new ApiUnreachableError(cause)
  }
}

export async function readErrorMessage(response: Response, fallback: string): Promise<string> {
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

  return `${fallback} (HTTP ${response.status})`
}
