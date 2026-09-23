import { coalescedRead } from './coalescedRead'
import { captureRequestContext, record, startRequest } from '../diagnostics/diagnostics'
import type { RequestContext } from '../diagnostics/diagnostics'

/** The request never reached the API, as opposed to an error response. */
export class ApiUnreachableError extends Error {
  constructor(cause: unknown) {
    // The browser's own wording ("Failed to fetch") differs per engine and means nothing.
    super('No connection to the server', { cause })
    this.name = 'ApiUnreachableError'
  }
}

const responseFields = new WeakMap<Response, ReturnType<typeof startRequest>['fields']>()
const reads = new Map<string, ReturnType<typeof coalescedRead<Response>>>()
let sessionGeneration = 0

export function clearResourceReads() {
  sessionGeneration++
  for (const read of reads.values()) read.clear()
  reads.clear()
}

export async function apiFetch(path: string, init?: RequestInit, context?: RequestContext): Promise<Response> {
  const captured = captureRequestContext(context)
  const coordinated = (init?.method ?? 'GET') === 'GET' && !init?.signal &&
    (path.startsWith('/api/photo-analysis') || path === '/api/jobs/summary')
  if (!coordinated) return send(path, init, context)
  let read = reads.get(path)
  if (!read) {
    read = coalescedRead<Response>()
    reads.set(path, read)
  }
  const version = sessionGeneration
  try {
    const response = await read.read(async () => {
      const response = await send(path, init, captured)
      const started = performance.now()
      const body = await response.arrayBuffer()
      const buffered = new Response([204, 205, 304].includes(response.status) ? null : body,
        { status: response.status, statusText: response.statusText, headers: response.headers })
      const readJson = buffered.json.bind(buffered)
      let parsed: Promise<unknown> | undefined
      buffered.json = () => parsed ??= readJson().then(value => {
        record('http.body-read', { ...responseFields.get(response), durationMs: Math.round(performance.now() - started) }, 'debug')
        return value
      }).catch((error: unknown) => {
        record('http.body-failed', { ...responseFields.get(response), errorType: error instanceof Error ? error.name : 'Unknown', durationMs: Math.round(performance.now() - started) }, 'warning')
        throw error
      })
      return buffered
    }, captured.trigger !== 'poll' && captured.trigger !== 'mount')
    if (version !== sessionGeneration) throw new Error('Session changed')
    const copy = response.clone()
    copy.json = () => response.json()
    return copy
  } finally {
    if (read.idle && reads.get(path) === read) reads.delete(path)
  }
}

async function send(path: string, init?: RequestInit, context?: RequestContext): Promise<Response> {
  const diagnostic = startRequest(init?.method ?? 'GET', path, context)
  const headers = new Headers(init?.headers)
  for (const [name, value] of Object.entries(diagnostic.headers)) headers.set(name, value)
  try {
    const response = await fetch(path, { ...init, headers })
    diagnostic.finish(response.status)
    responseFields.set(response, diagnostic.fields)
    const readJson = response.json.bind(response)
    response.json = async () => {
      const started = performance.now()
      try {
        const value: unknown = await readJson()
        record('http.body-read', { ...diagnostic.fields, durationMs: Math.round(performance.now() - started) }, 'debug')
        return value
      } catch (error) {
        record('http.body-failed', { ...diagnostic.fields, errorType: error instanceof Error ? error.name : 'Unknown', durationMs: Math.round(performance.now() - started) }, 'warning')
        throw error
      }
    }
    return response
  } catch (cause) {
    diagnostic.finish(0, init?.signal?.aborted ? 'cancelled' : 'network-error')
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
