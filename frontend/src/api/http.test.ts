import { afterEach, beforeEach, expect, it, vi } from 'vitest'

beforeEach(() => { vi.resetModules(); vi.stubGlobal('fetch', vi.fn()) })
afterEach(() => vi.unstubAllGlobals())

it('propagates diagnostic headers without changing the response body', async () => {
  vi.mocked(fetch).mockResolvedValue(new Response('{"ok":true}'))
  const { apiFetch } = await import('./http')
  const response = await apiFetch('/api/assets', { headers: { Accept: 'application/json' } }, { source: 'AssetList', trigger: 'sse', causationId: 'event-1' })
  expect(await response.json()).toEqual({ ok: true })
  const headers = new Headers(vi.mocked(fetch).mock.calls[0][1]?.headers)
  expect(headers.get('Accept')).toBe('application/json')
  expect(headers.get('X-Request-Source')).toBe('AssetList')
  expect(headers.get('X-Causation-Id')).toBe('event-1')
  expect(headers.get('traceparent')).toMatch(/^00-[a-f0-9]{32}-[a-f0-9]{16}-01$/)
})

it('keeps HTTP error responses distinct from network failures', async () => {
  const { apiFetch, ApiUnreachableError } = await import('./http')
  vi.mocked(fetch).mockResolvedValue(new Response('unavailable', { status: 503 }))
  expect((await apiFetch('/api/assets')).status).toBe(503)
  vi.mocked(fetch).mockRejectedValue(new TypeError('Network failed'))
  await expect(apiFetch('/api/assets')).rejects.toBeInstanceOf(ApiUnreachableError)
})

it('reports invalid JSON without copying its body into diagnostics', async () => {
  vi.mocked(fetch).mockResolvedValue(new Response('private document text'))
  const { apiFetch } = await import('./http')
  const { exportDiagnostics } = await import('../diagnostics/diagnostics')
  const response = await apiFetch('/api/assets')
  await expect(response.json()).rejects.toBeInstanceOf(SyntaxError)
  expect(exportDiagnostics()).toContain('http.body-failed')
  expect(exportDiagnostics()).not.toContain('private document text')
})
