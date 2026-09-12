/** Mirrors ChangeEvent in backend Core/RealTime. */
export interface ResourceChange {
  resource: string
  action: string
  id: string | null
}

/** `null` = the stream just (re)connected; re-read the collection, nothing was queued. */
export type ChangeListener = (change: ResourceChange | null) => void

/** `paused` = deliberately closed (nobody listening / tab hidden / idle). `offline` = no answer. */
export type ConnectionStatus = 'online' | 'offline' | 'paused'

export type ConnectionListener = (status: ConnectionStatus) => void

/** Long enough to sit out an Alt-Tab without tearing the stream down. */
const HIDDEN_GRACE_MS = 30_000

/** A tab left on screen but untouched stops holding a connection. */
const IDLE_LIMIT_MS = 15 * 60_000

/** The browser retries a drop itself, but gives up for good on an HTTP error. */
const REOPEN_MIN_MS = 2_000
const REOPEN_MAX_MS = 60_000

/** A reconnect quicker than this stays silent; a slower one raises the banner. */
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

/** One EventSource per tab: a browser allows six connections per origin. */
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

/** Calls the listener at once with the current status. */
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

/** Open/close to match: someone listening, tab visible, user not idle. */
function sync() {
  const wanted =
    listeners.size > 0 && document.visibilityState === 'visible' && !hasGoneIdle()

  if (wanted && source === null && reopenTimer === undefined) {
    open()
  } else if (!wanted) {
    // Also cancels a pending reconnect nobody is waiting on.
    close()
  }
}

function open() {
  source = new EventSource('/api/events')

  // A slow reconnect (Render cold start) should raise the banner; a quick one should not.
  // The first connection is exempt - the page shows its own "loading" then.
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

    // Not on the first connection: a component loads its own data on mount.
    if (hasConnected) {
      dispatch(null)
    }

    hasConnected = true
  }

  source.onmessage = (event: MessageEvent<string>) => {
    try {
      dispatch(JSON.parse(event.data) as ResourceChange)
    } catch {
      // A truncated frame is not worth dropping the stream.
    }
  }

  source.onerror = () => {
    clearTimer(connectingTimer)
    connectingTimer = undefined

    // CONNECTING = browser retrying; the page is just as cut off as on a hard fail.
    setStatus('offline')

    // CLOSED = browser will not retry (HTTP error: 502 on API restart, dead session).
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

// A returning tab should not sit out the rest of a backoff already in flight - the reason
// for that delay (repeated failures with nobody watching) no longer applies.
function retryNow() {
  clearTimer(reopenTimer)
  reopenTimer = undefined
  reopenDelayMs = REOPEN_MIN_MS
  sync()
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

  // visibilitychange, not blur: blur also fires for a window still visible on a 2nd monitor.
  document.addEventListener('visibilitychange', () => {
    clearTimer(hiddenTimer)
    hiddenTimer = undefined

    if (document.visibilityState === 'visible') {
      markActive()
      retryNow()
    } else {
      hiddenTimer = window.setTimeout(sync, HIDDEN_GRACE_MS)
    }
  })

  ACTIVITY_EVENTS.forEach((name) =>
    // Passive: these fire constantly, must not delay scrolling.
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
