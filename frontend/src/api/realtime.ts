/** Mirrors ChangeEvent in backend/Infrastructure/RealTime. */
export interface ResourceChange {
  resource: string
  action: string
  id: string | null
}

/** `null` means the stream just (re)connected: anything that happened while it was down was
 *  never queued, so the listener has to re-read its collection. */
export type ChangeListener = (change: ResourceChange | null) => void

/**
 * `paused` is not a failure: the stream is deliberately closed while nobody is listening,
 * the tab is hidden or the user has gone idle. Only `offline` means the API did not answer.
 */
export type ConnectionStatus = 'online' | 'offline' | 'paused'

export type ConnectionListener = (status: ConnectionStatus) => void

/** Long enough to sit out an Alt-Tab without tearing the stream down and building it up again. */
const HIDDEN_GRACE_MS = 30_000

/** A tab left open on screen but untouched stops holding a connection. */
const IDLE_LIMIT_MS = 15 * 60_000

/** The browser retries a dropped connection itself, but gives up for good on an HTTP error. */
const REOPEN_MIN_MS = 2_000
const REOPEN_MAX_MS = 60_000

/** A reconnect quicker than this stays silent; a slower one raises the outage banner. */
const RECONNECT_GRACE_MS = 1_500

const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'scroll', 'wheel'] as const

const listeners = new Map<string, Set<ChangeListener>>()
const connectionListeners = new Set<ConnectionListener>()

let status: ConnectionStatus = 'paused'

let source: EventSource | null = null
let hasConnected = false
let lastActivityAt = Date.now()
let idleTimer: number | undefined
let hiddenTimer: number | undefined
let connectingTimer: number | undefined
let reopenTimer: number | undefined
let reopenDelayMs = REOPEN_MIN_MS

/**
 * Subscribes to changes of one collection. The whole tab shares a single EventSource: over
 * HTTP/1.1 a browser allows six connections per origin, and a stream per component would
 * spend that budget on nothing.
 */
export function subscribeToChanges(resource: string, listener: ChangeListener): () => void {
  const forResource = listeners.get(resource) ?? new Set<ChangeListener>()
  forResource.add(listener)
  listeners.set(resource, forResource)

  startWatchingTab()
  markActive()
  sync()

  return () => {
    forResource.delete(listener)

    if (forResource.size === 0) {
      listeners.delete(resource)
    }

    sync()
  }
}

/**
 * Reports whether the stream is up. The listener is called at once with the current status,
 * so a component that mounts into an already broken connection still learns about it.
 */
export function subscribeToConnection(listener: ConnectionListener): () => void {
  connectionListeners.add(listener)
  listener(status)

  return () => {
    connectionListeners.delete(listener)
  }
}

function setStatus(next: ConnectionStatus) {
  if (next === status) {
    return
  }

  status = next
  connectionListeners.forEach((listener) => listener(next))
}

/**
 * Opens or closes the stream to match the three conditions worth holding one open for:
 * somebody is listening, the tab is on screen, and the user is still around. Every event
 * handler below just changes one of those and calls back in here.
 */
function sync() {
  const wanted =
    listeners.size > 0 && document.visibilityState === 'visible' && !hasGoneIdle()

  if (wanted && source === null && reopenTimer === undefined) {
    open()
  } else if (!wanted) {
    // Also when there is no stream to close: a reconnect may be pending, and a stream nobody
    // wants must not report itself offline while it waits out a backoff nobody is watching.
    close()
  }
}

function open() {
  source = new EventSource('/api/events')

  // A reconnect that drags on is worth surfacing: locally it means nothing, but in production
  // the API scales to zero and a cold start takes the better part of a minute, during which
  // the page would otherwise look connected and just be stale. The first connection is exempt
  // - the page is already showing its own "loading" then.
  clearTimer(connectingTimer)
  connectingTimer = hasConnected
    ? window.setTimeout(() => {
        if (source?.readyState === EventSource.CONNECTING) {
          setStatus('offline')
        }
      }, RECONNECT_GRACE_MS)
    : undefined

  source.onopen = () => {
    clearTimer(connectingTimer)
    connectingTimer = undefined
    reopenDelayMs = REOPEN_MIN_MS
    setStatus('online')

    // Skipped on the very first connection: a component loads its own data when it mounts,
    // and this would only duplicate that request.
    if (hasConnected) {
      dispatch(null)
    }

    hasConnected = true
  }

  source.onmessage = (event: MessageEvent<string>) => {
    try {
      dispatch(JSON.parse(event.data) as ResourceChange)
    } catch {
      // A truncated frame is not worth taking the stream down for.
    }
  }

  source.onerror = () => {
    clearTimer(connectingTimer)
    connectingTimer = undefined

    // Offline either way: CONNECTING means the browser is retrying on its own, and until it
    // succeeds the page is just as cut off as on a hard failure.
    setStatus('offline')

    // CLOSED means the browser refuses to retry: an HTTP error, typically a 502 while the
    // API restarts or an expired session.
    if (source?.readyState === EventSource.CLOSED) {
      reopenLater()
    }
  }
}

function close() {
  source?.close()
  source = null
  clearTimer(connectingTimer)
  connectingTimer = undefined
  clearTimer(reopenTimer)
  reopenTimer = undefined
  reopenDelayMs = REOPEN_MIN_MS
  setStatus('paused')
}

function reopenLater() {
  source?.close()
  source = null

  if (listeners.size === 0 || reopenTimer !== undefined) {
    return
  }

  reopenTimer = window.setTimeout(() => {
    reopenTimer = undefined
    sync()
  }, reopenDelayMs)

  reopenDelayMs = Math.min(reopenDelayMs * 2, REOPEN_MAX_MS)
}

function dispatch(change: ResourceChange | null) {
  if (change === null) {
    listeners.forEach((forResource) => forResource.forEach((listener) => listener(null)))
    return
  }

  listeners.get(change.resource)?.forEach((listener) => listener(change))
}

let watching = false

function startWatchingTab() {
  if (watching) {
    return
  }

  watching = true

  // visibilitychange, not window blur: blur also fires when the window is still on screen on
  // a second monitor and the user clicked into another app, and a page in plain sight should
  // keep updating.
  document.addEventListener('visibilitychange', () => {
    clearTimer(hiddenTimer)
    hiddenTimer = undefined

    if (document.visibilityState === 'visible') {
      markActive()
      sync()
    } else {
      hiddenTimer = window.setTimeout(sync, HIDDEN_GRACE_MS)
    }
  })

  ACTIVITY_EVENTS.forEach((name) =>
    // Passive: these fire constantly and must never delay scrolling.
    window.addEventListener(name, markActive, { passive: true }),
  )
}

function markActive() {
  lastActivityAt = Date.now()

  clearTimer(idleTimer)
  idleTimer = window.setTimeout(sync, IDLE_LIMIT_MS)

  if (source === null) {
    sync()
  }
}

function hasGoneIdle(): boolean {
  return Date.now() - lastActivityAt >= IDLE_LIMIT_MS
}

function clearTimer(timer: number | undefined) {
  if (timer !== undefined) {
    window.clearTimeout(timer)
  }
}
