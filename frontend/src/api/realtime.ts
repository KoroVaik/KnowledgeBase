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

export interface ConnectionState {
  status: ConnectionStatus
  reconnectAt: number | null
}

export type ConnectionListener = (state: ConnectionState) => void

/** Long enough to sit out an Alt-Tab without tearing the stream down. */
const HIDDEN_GRACE_MS = 30_000

/** A tab left on screen but untouched stops holding a connection. */
const IDLE_LIMIT_MS = 15 * 60_000

/** Every retry is scheduled here, so the UI can show its exact time. */
const REOPEN_MIN_MS = 2_000
const REOPEN_MAX_MS = 60_000

/** A reconnect quicker than this stays silent; a slower one raises the banner. */
const RECONNECT_GRACE_MS = 1_500

const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'scroll', 'wheel'] as const

const listeners = new Map<string, Set<ChangeListener>>()
const connectionListeners = new Set<ConnectionListener>()

let status: ConnectionStatus = 'paused'
let reconnectAt: number | null = null

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

/** Calls the listener at once with the current stream state. */
export function subscribeToConnection(listener: ConnectionListener): () => void {
  connectionListeners.add(listener)
  listener({ status, reconnectAt })

  return () => {
    connectionListeners.delete(listener)
  }
}

function setConnection(nextStatus: ConnectionStatus, nextReconnectAt = reconnectAt) {
  if (nextStatus === status && nextReconnectAt === reconnectAt) {
    return
  }

  status = nextStatus
  reconnectAt = nextReconnectAt
  connectionListeners.forEach((listener) => listener({ status, reconnectAt }))
}

/** Open/close to match: someone listening, tab visible, user not idle. */
function sync() {
  const wanted = isWanted()

  if (wanted && source === null && reopenTimer === undefined) {
    open()
  } else if (!wanted) {
    // Also cancels a pending reconnect nobody is waiting on.
    close()
  }
}

function open() {
  const nextSource = new EventSource('/api/events')
  source = nextSource
  setConnection(status, null)

  // A slow reconnect (Render cold start) should raise the banner; a quick one should not.
  // The first connection is exempt - the page shows its own "loading" then.
  clearTimer(connectingTimer)
  connectingTimer = hasConnected
    ? window.setTimeout(() => {
        if (source === nextSource && nextSource.readyState === EventSource.CONNECTING) {
          connectingTimer = undefined
          reopenLater(nextSource)
        }
      }, RECONNECT_GRACE_MS)
    : undefined

  nextSource.onopen = () => {
    if (source !== nextSource) {
      return
    }

    clearTimer(connectingTimer)
    connectingTimer = undefined
    reopenDelayMs = REOPEN_MIN_MS
    setConnection('online', null)

    // Not on the first connection: a component loads its own data on mount.
    if (hasConnected) {
      dispatch(null)
    }

    hasConnected = true
  }

  nextSource.onmessage = (event: MessageEvent<string>) => {
    if (source !== nextSource) {
      return
    }

    try {
      dispatch(JSON.parse(event.data) as ResourceChange)
    } catch {
      // A truncated frame is not worth dropping the stream.
    }
  }

  nextSource.onerror = () => {
    if (source !== nextSource) {
      return
    }

    clearTimer(connectingTimer)
    connectingTimer = undefined
    reopenLater(nextSource)
  }
}

function close() {
  const currentSource = source
  source = null
  currentSource?.close()
  clearTimer(connectingTimer)
  connectingTimer = undefined
  clearTimer(reopenTimer)
  reopenTimer = undefined
  reopenDelayMs = REOPEN_MIN_MS
  setConnection('paused', null)
}

// A returning tab should not sit out a retry scheduled while it was in the background.
function retryNow() {
  clearTimer(reopenTimer)
  reopenTimer = undefined
  reopenDelayMs = REOPEN_MIN_MS
  setConnection(status, null)

  if (status === 'offline') {
    const currentSource = source
    source = null
    currentSource?.close()
    clearTimer(connectingTimer)
    connectingTimer = undefined
  }

  sync()
}

/** Cancels the scheduled wait and immediately opens a fresh stream. */
export function reconnectNow() {
  if (status === 'offline') {
    retryNow()
  }
}

function reopenLater(failedSource: EventSource) {
  if (source === failedSource) {
    source = null
  }

  failedSource.close()

  if (!isWanted()) {
    sync()
    return
  }

  if (reopenTimer !== undefined) {
    return
  }

  const delayMs = reopenDelayMs
  const nextReconnectAt = Date.now() + delayMs

  reopenTimer = window.setTimeout(() => {
    reopenTimer = undefined
    setConnection(status, null)
    sync()
  }, delayMs)

  reopenDelayMs = Math.min(reopenDelayMs * 2, REOPEN_MAX_MS)
  setConnection('offline', nextReconnectAt)
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

  if (status === 'offline' && reopenTimer !== undefined) {
    retryNow()
  } else if (source === null) {
    sync()
  }
}

function isWanted(): boolean {
  return listeners.size > 0 && document.visibilityState === 'visible' && !hasGoneIdle()
}

function hasGoneIdle(): boolean {
  return Date.now() - lastActivityAt >= IDLE_LIMIT_MS
}

function clearTimer(timer: number | undefined) {
  if (timer !== undefined) {
    window.clearTimeout(timer)
  }
}
