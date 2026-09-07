/** Mirrors ChangeEvent in backend/Infrastructure/RealTime. */
export interface ResourceChange {
  resource: string
  action: string
  id: string | null
}

/** `null` means the stream just (re)connected: anything that happened while it was down was
 *  never queued, so the listener has to re-read its collection. */
export type ChangeListener = (change: ResourceChange | null) => void

/** Long enough to sit out an Alt-Tab without tearing the stream down and building it up again. */
const HIDDEN_GRACE_MS = 30_000

/** A tab left open on screen but untouched stops holding a connection. */
const IDLE_LIMIT_MS = 15 * 60_000

/** The browser retries a dropped connection itself, but gives up for good on an HTTP error. */
const REOPEN_MIN_MS = 2_000
const REOPEN_MAX_MS = 60_000

const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'scroll', 'wheel'] as const

const listeners = new Map<string, Set<ChangeListener>>()

let source: EventSource | null = null
let hasConnected = false
let lastActivityAt = Date.now()
let idleTimer: number | undefined
let hiddenTimer: number | undefined
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
 * Opens or closes the stream to match the three conditions worth holding one open for:
 * somebody is listening, the tab is on screen, and the user is still around. Every event
 * handler below just changes one of those and calls back in here.
 */
function sync() {
  const wanted =
    listeners.size > 0 && document.visibilityState === 'visible' && !hasGoneIdle()

  if (wanted && source === null && reopenTimer === undefined) {
    open()
  } else if (!wanted && source !== null) {
    close()
  }
}

function open() {
  source = new EventSource('/api/events')

  source.onopen = () => {
    reopenDelayMs = REOPEN_MIN_MS

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
    // A dropped connection the browser retries on its own (readyState stays CONNECTING).
    // CLOSED means it refuses to: an HTTP error, typically a 502 while the API restarts or
    // an expired session.
    if (source?.readyState === EventSource.CLOSED) {
      reopenLater()
    }
  }
}

function close() {
  source?.close()
  source = null
  clearTimer(reopenTimer)
  reopenTimer = undefined
  reopenDelayMs = REOPEN_MIN_MS
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
