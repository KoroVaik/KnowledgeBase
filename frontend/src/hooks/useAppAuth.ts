import { useEffect, useRef, useState } from 'react'
import { fetchCurrentUser, logout } from '../api/auth'
import type { CurrentUser } from '../api/auth'
import { fetchFeatures } from '../api/features'
import type { FeatureFlags } from '../api/features'
import { reconnectNow } from '../api/realtime'
import { useConnectionStatus } from './useConnectionStatus'

export type AuthState =
  | { status: 'checking' }
  | { status: 'anonymous'; message?: string }
  | { status: 'authenticated'; user: CurrentUser }
  | { status: 'unreachable'; message: string }

/** State behind App: the session (cookie-based, checked once and on stream recovery), the
 *  feature flags, the connection banner, and the upload counter AssetList reloads on. */
export function useAppAuth() {
  const [auth, setAuth] = useState<AuthState>({ status: 'checking' })
  const [uploadCount, setUploadCount] = useState(0)
  // Bumped by "Try again" to re-run the load effect.
  const [attempt, setAttempt] = useState(0)
  // Assume Google sign-in off until the flags arrive: showing a button the server has no
  // handler for is worse than a brief absence. maxSourceChars 0 keeps the size hint off.
  const [features, setFeatures] = useState<FeatureFlags>({
    googleSignInEnabled: false,
    maxSourceChars: 0,
    maxUploadBytes: 0,
  })

  const { status: connection, reconnectAt, observedAt } = useConnectionStatus()
  const reconnectInSeconds = useReconnectCountdown(reconnectAt, observedAt)

  useEffect(() => {
    let cancelled = false

    void fetchCurrentUser()
      .then((user) => {
        if (!cancelled) {
          setAuth(user === null ? { status: 'anonymous' } : { status: 'authenticated', user })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          // Not `anonymous`: only a 401 means "nobody signed in" (and that returns null).
          // Anything else leaves the session unknown - nothing to sign in to.
          setAuth({ status: 'unreachable', message: messageOf(error) })
        }
      })

    void fetchFeatures()
      .then((flags) => {
        if (!cancelled) {
          setFeatures(flags)
        }
      })
      .catch(() => {
        // Leave every flag off - the server refuses a switched-off feature anyway.
      })

    return () => {
      cancelled = true
    }
  }, [attempt])

  // The cookie can expire while the tab is backgrounded. Re-check on stream recovery, but
  // quietly: no blank "checking", and a failure is left to the connection banner.
  const seenOnline = useRef(false)

  useEffect(() => {
    if (connection !== 'online') {
      return
    }

    if (seenOnline.current) {
      void fetchCurrentUser()
        .then((user) => {
          setAuth(user === null ? { status: 'anonymous' } : { status: 'authenticated', user })
        })
        .catch(() => {
          // Keep whatever is on screen - the banner already reports the outage.
        })

      void fetchFeatures()
        .then(setFeatures)
        .catch(() => {})
    }

    seenOnline.current = true
  }, [connection])

  // EventSource does not reveal an HTTP status. A single ordinary request tells a lost
  // session from a dropped stream without treating a live API as offline.
  useEffect(() => {
    if (connection !== 'offline' || auth.status !== 'authenticated') {
      return
    }

    let cancelled = false

    void fetchCurrentUser()
      .then((user) => {
        if (!cancelled && user === null) {
          setAuth({ status: 'anonymous', message: 'Your session has expired. Please sign in again.' })
        }
      })
      .catch(() => {
        // The stream retry already communicates that the API cannot currently be reached.
      })

    return () => {
      cancelled = true
    }
  }, [auth.status, connection])

  // "checking" is set here, not in the effect: the click is what triggers the re-check.
  function retry() {
    setAuth({ status: 'checking' })
    setAttempt((count) => count + 1)
  }

  async function handleSignOut() {
    try {
      await logout()
    } finally {
      setAuth({ status: 'anonymous' })
    }
  }

  function markSignedIn(user: CurrentUser) {
    setAuth({ status: 'authenticated', user })
  }

  function bumpUploadCount() {
    setUploadCount((count) => count + 1)
  }

  function reconnect() {
    reconnectNow()
  }

  return {
    auth,
    features,
    connection,
    reconnectInSeconds,
    uploadCount,
    retry,
    reconnect,
    handleSignOut,
    markSignedIn,
    bumpUploadCount,
  }
}

function useReconnectCountdown(reconnectAt: number | null, observedAt: number): number | null {
  const [now, setNow] = useState(0)

  useEffect(() => {
    if (reconnectAt === null) {
      return
    }

    const timer = window.setInterval(() => setNow(Date.now()), 250)

    return () => window.clearInterval(timer)
  }, [reconnectAt])

  if (reconnectAt === null || !Number.isFinite(observedAt)) {
    return null
  }

  return Math.max(0, Math.ceil((reconnectAt - Math.max(now, observedAt)) / 1_000))
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
