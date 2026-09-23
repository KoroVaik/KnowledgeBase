import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

beforeEach(() => {
  vi.resetModules()
  vi.useFakeTimers()
  vi.stubGlobal('navigator', { onLine: true })
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))
})
afterEach(() => { vi.clearAllTimers(); vi.useRealTimers(); vi.unstubAllGlobals() })

describe('diagnostics delivery', () => {
  it('detects requests still waiting for a response', async () => {
    vi.stubGlobal('window', new EventTarget())
    const diagnostics = await import('./diagnostics')
    diagnostics.initializeDiagnostics()
    for (let i = 0; i < 2000; i++) diagnostics.startRequest('GET', '/api/assets', { source: 'AssetList', trigger: 'sse' })
    await vi.advanceTimersByTimeAsync(10_000)
    const events = vi.mocked(fetch).mock.calls.flatMap(call => JSON.parse(call[1]!.body as string).events)
    expect(events.find((entry: { event: string }) => entry.event === 'http.storm').fields).toMatchObject({ count: 2000, inFlight: 2000, completed: 0 })
  })

  it('bounds a request storm and sends one batch at a time without logging its own transport', async () => {
    const diagnostics = await import('./diagnostics')
    for (let i = 0; i < 2000; i++) diagnostics.startRequest('GET', '/api/assets').finish(200)
    expect(JSON.parse(diagnostics.exportDiagnostics()).events).toHaveLength(500)
    const pending = diagnostics.flushDiagnostics()
    await diagnostics.flushDiagnostics()
    await pending
    expect(fetch).toHaveBeenCalledTimes(1)
    const body = JSON.parse(vi.mocked(fetch).mock.calls[0][1]!.body as string)
    expect(body.events).toHaveLength(50)
    expect(body.events.every((entry: { event: string }) => entry.event === 'http.completed')).toBe(true)
  })

  it('backs off after a delivery failure and retains a bounded retry batch', async () => {
    vi.mocked(fetch).mockRejectedValue(new Error('offline'))
    const diagnostics = await import('./diagnostics')
    diagnostics.record('upload.started')
    await diagnostics.flushDiagnostics()
    await diagnostics.flushDiagnostics()
    expect(fetch).toHaveBeenCalledTimes(1)
    vi.advanceTimersByTime(4000)
    vi.mocked(fetch).mockResolvedValue(new Response(null, { status: 204 }))
    await diagnostics.flushDiagnostics()
    expect(fetch).toHaveBeenCalledTimes(2)
  })

  it('keeps request identity and source while removing signatures from logs', async () => {
    const diagnostics = await import('./diagnostics')
    const request = diagnostics.startRequest('GET', 'https://bucket.example/photo?X-Amz-Signature=private', {
      source: 'FacePreview', trigger: 'asset-change', uploadId: 'upload-1',
    })
    expect(request.headers['X-Upload-Id']).toBe('upload-1')
    expect(request.headers.traceparent).toMatch(/^00-[a-f0-9]{32}-[a-f0-9]{16}-01$/)
    request.finish(200)
    request.finish(500)
    const exported = diagnostics.exportDiagnostics()
    expect(exported).not.toContain('private')
    expect(JSON.parse(exported).events).toHaveLength(1)
    expect(exported).toContain('FacePreview')
    expect(diagnostics.redact('Password=private; Bearer private')).not.toContain('private')
  })

  it('preserves critical errors when the ordinary event buffer is full', async () => {
    const diagnostics = await import('./diagnostics')
    for (let i = 0; i < 600; i++) diagnostics.record('http.completed')
    diagnostics.record('upload.failed', { errorType: 'NetworkError' }, 'error')
    for (let i = 0; i < 10; i++) { await diagnostics.flushDiagnostics(); vi.advanceTimersByTime(2000) }
    const events = vi.mocked(fetch).mock.calls.flatMap(call => JSON.parse(call[1]!.body as string).events)
    expect(events.some((entry: { event: string }) => entry.event === 'upload.failed')).toBe(true)
    expect(events).toHaveLength(500)
  })
})
