export type RequestContext = {
  source?: string
  trigger?: string
  uploadBatchId?: string
  uploadId?: string
  assetId?: string
  causationId?: string
  traceId?: string
  jobId?: string
}

type Fields = Record<string, string | number | boolean | null | undefined>
type Diagnostic = { event: string; timestamp: string; level: string; fields: Fields }
const MAX_BUFFER = 500
const MAX_BATCH = 50
const MAX_BATCH_BYTES = 48_000
const MAX_AGE_MS = 120_000
const enabled = import.meta.env.VITE_DIAGNOSTICS_ENABLED !== 'false'
const debug = import.meta.env.VITE_DIAGNOSTICS_DEBUG === 'true'
const queue: Diagnostic[] = []
const recent: Diagnostic[] = []
const rates = new Map<string, { count: number; completed: number; failed: number; maxDurationMs: number }>()
let current: RequestContext = {}
let sequence = 0
let dropped = 0
let inFlight = 0
let flushing = false
let nextFlushAt = 0
let retryDelay = 2000
let initialized = false
let timer: ReturnType<typeof setInterval> | undefined
let observer: PerformanceObserver | undefined

function rateFor(key: string) {
  if (!rates.has(key) && rates.size >= 100) key = 'other|other|other|other'
  const rate = rates.get(key) ?? { count: 0, completed: 0, failed: 0, maxDurationMs: 0 }
  rates.set(key, rate)
  return rate
}

export function diagnosticId(bytes = 16): string {
  const values = new Uint8Array(bytes)
  crypto.getRandomValues(values)
  return Array.from(values, value => value.toString(16).padStart(2, '0')).join('')
}

export const sessionId = diagnosticId()

export function redact(value: string): string {
  return value.slice(0, 2048)
    .replace(/(https?:\/\/[^\s?'"<>]+)\?[^\s'"<>]*/gi, '$1?[redacted]')
    .replace(/\b(password|pwd|authorization|cookie|set-cookie|x-ingest-token|api[-_]?key|secret|token|signature)["']?\s*[:=]\s*(?:"[^"]*"|'[^']*'|[^\s;,]+)/gi, '$1=[redacted]')
    .replace(/Bearer\s+[^\s,'";]+/gi, 'Bearer [redacted]')
}

export function captureRequestContext(context?: RequestContext): RequestContext {
  return { source: 'api', trigger: 'action', ...current, ...context }
}

export function requestContext(source: string, trigger = 'action'): RequestContext {
  return { ...current, source, trigger: current.trigger ?? trigger }
}

// Context applies only while starting requests; asynchronous continuations pass it explicitly.
export function withRequestContext<T>(context: RequestContext, action: () => T): T {
  const previous = current
  current = { ...current, ...context }
  try { return action() } finally { current = previous }
}

export function record(event: string, fields: Fields = {}, level = 'information'): void {
  if (!enabled || level === 'debug' && !debug) return
  const safe = Object.fromEntries(Object.entries(fields).slice(0, 22).map(([key, value]) => [key, typeof value === 'string' ? redact(value) : value]))
  const item = { event, timestamp: new Date().toISOString(), level, fields: { ...safe, release: import.meta.env.VITE_RELEASE ?? import.meta.env.MODE, sequence: ++sequence } }
  if (recent.length >= MAX_BUFFER) recent.shift()
  recent.push(item)
  if (queue.length >= MAX_BUFFER) {
    if (level !== 'error' && level !== 'warning') { dropped++; return }
    const discard = queue.findIndex(entry => entry.level !== 'error' && entry.level !== 'warning')
    queue.splice(discard < 0 ? 0 : discard, 1)
    dropped++
  }
  queue.push(item)
}

export function startRequest(method: string, target: string, context: RequestContext = current) {
  const path = target.split(/[?#]/, 1)[0]
  const route = path.replace(/[a-f\d]{32}(?:\.[a-z\d]+)?/gi, ':id')
  const requestId = diagnosticId()
  const traceId = context.traceId ?? diagnosticId()
  const spanId = diagnosticId(8)
  const fields = { ...context, source: context.source ?? 'api', trigger: context.trigger ?? 'action', requestId, traceId, spanId, method, route }
  const rateKey = `${fields.source}|${fields.trigger}|${method}|${route}`
  rateFor(rateKey).count++
  const started = performance.now()
  inFlight++
  record('http.started', { ...fields, inFlight }, 'debug')
  let finished = false
  return {
    fields,
    context: { ...context, traceId },
    headers: {
      traceparent: `00-${traceId}-${spanId}-01`,
      'X-Session-Id': sessionId,
      'X-Request-Id': requestId,
      'X-Request-Source': fields.source,
      'X-Request-Trigger': fields.trigger,
      ...(context.uploadBatchId ? { 'X-Upload-Batch-Id': context.uploadBatchId } : {}),
      ...(context.uploadId ? { 'X-Upload-Id': context.uploadId } : {}),
      ...(context.causationId ? { 'X-Causation-Id': context.causationId } : {}),
    },
    finish(status: number, outcome = 'completed', bytes?: number) {
      if (finished) return
      finished = true
      inFlight--
      const durationMs = Math.round(performance.now() - started)
      const rate = rateFor(rateKey)
      rate.completed++
      if (status === 0 || status >= 400) rate.failed++
      rate.maxDurationMs = Math.max(rate.maxDurationMs, durationMs)
      record('http.completed', { ...fields, status, outcome, durationMs, bytes, inFlight },
        outcome === 'cancelled' ? 'debug' : status === 0 || status >= 500 ? 'error' : status >= 400 ? 'warning' : 'information')
    },
  }
}

export async function flushDiagnostics(): Promise<void> {
  if (!enabled || flushing || Date.now() < nextFlushAt || !navigator.onLine || queue.length === 0) return
  while (queue.length && Date.now() - Date.parse(queue[0].timestamp) > MAX_AGE_MS) { queue.shift(); dropped++ }
  if (!queue.length) return
  const batch: Diagnostic[] = []
  let bytes = 200
  while (queue.length && batch.length < MAX_BATCH) {
    const size = new TextEncoder().encode(JSON.stringify(queue[0])).byteLength
    if (size > MAX_BATCH_BYTES - 200) { queue.shift(); dropped++; continue }
    if (bytes + size > MAX_BATCH_BYTES) break
    bytes += size
    batch.push(queue.shift()!)
  }
  if (batch.length === 0) return
  flushing = true
  nextFlushAt = Date.now() + 2000
  try {
    // The diagnostics transport deliberately bypasses apiFetch to prevent recursive logging.
    const response = await fetch('/api/diagnostics/events', {
      method: 'POST', credentials: 'same-origin', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sessionId, events: batch }), signal: AbortSignal.timeout(5000),
    })
    if (!response.ok) {
      if (response.status === 400 || response.status === 413) { dropped += batch.length; return }
      if (response.status === 401 || response.status === 403) { dropped += batch.length; nextFlushAt = Date.now() + 30_000; return }
      throw new Error('Diagnostic delivery unavailable')
    }
    retryDelay = 2000
  } catch {
    const available = MAX_BUFFER - queue.length
    queue.unshift(...batch.slice(0, available))
    dropped += Math.max(0, batch.length - available)
    retryDelay = Math.min(retryDelay * 2, 60_000)
    nextFlushAt = Date.now() + retryDelay
  } finally { flushing = false }
}

export function exportDiagnostics(): string {
  return JSON.stringify({ sessionId, dropped, events: recent }, null, 2)
}

export function initializeDiagnostics(): void {
  if (initialized || !enabled) return
  initialized = true
  record('session.started')
  const error = (event: ErrorEvent) => record('javascript.error', { errorType: event.error instanceof Error ? event.error.name : 'Error', message: event.message, stack: event.error instanceof Error ? event.error.stack : undefined }, 'error')
  const rejection = (event: PromiseRejectionEvent) => record('javascript.rejection', {
    errorType: event.reason instanceof Error ? event.reason.name : 'UnhandledRejection',
    message: event.reason instanceof Error ? event.reason.message : 'Non-error rejection',
    stack: event.reason instanceof Error ? event.reason.stack : undefined,
  }, 'error')
  window.addEventListener('error', error)
  window.addEventListener('unhandledrejection', rejection)
  let ticks = 0
  timer = setInterval(() => {
    if (++ticks % 5 === 0) {
      for (const [key, rate] of rates) {
        const [source, trigger, method, route] = key.split('|')
        record(rate.count > 50 ? 'http.storm' : 'http.summary', { source, trigger, method, route, count: rate.count, completed: rate.completed, failed: rate.failed, maxDurationMs: rate.maxDurationMs, inFlight }, rate.count > 50 ? 'warning' : 'information')
      }
      rates.clear()
      if (dropped) { const count = dropped; dropped = 0; record('diagnostics.dropped', { dropped: count }, 'warning') }
    }
    void flushDiagnostics()
  }, 2000)
  if (debug && typeof PerformanceObserver !== 'undefined' && PerformanceObserver.supportedEntryTypes.includes('longtask')) {
    observer = new PerformanceObserver(list => {
      const entries = list.getEntries()
      record('browser.longtasks', { count: entries.length, maxDurationMs: Math.max(...entries.map(entry => entry.duration)) }, 'warning')
    })
    observer.observe({ entryTypes: ['longtask'] })
  }
  import.meta.hot?.dispose(() => {
    clearInterval(timer)
    observer?.disconnect()
    window.removeEventListener('error', error)
    window.removeEventListener('unhandledrejection', rejection)
  })
}
