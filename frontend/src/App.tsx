import { useEffect, useRef, useState } from 'react'
import { fetchCurrentUser, logout } from './api/auth'
import type { CurrentUser } from './api/auth'
import { fetchFeatures } from './api/features'
import type { FeatureFlags } from './api/features'
import { AssetList } from './components/AssetList'
import { LoginForm } from './components/LoginForm'
import { UploadDropZone } from './components/UploadDropZone'
import { NotesList } from './components/NotesList'
import { useConnectionStatus } from './hooks/useConnectionStatus'
import './App.css'

type AuthState =
  | { status: 'checking' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; user: CurrentUser }
  | { status: 'unreachable'; message: string }

function App() {
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

  const connection = useConnectionStatus()

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

  return (
    <main className="app">
      <header className="app-header">
        <h1>Knowledge Base</h1>
        {auth.status === 'authenticated' && (
          <button type="button" className="sign-out" onClick={handleSignOut}>
            Sign out
          </button>
        )}
      </header>

      {connection === 'offline' && (
        // role="status", not "alert": not a response to a user action.
        <p className="connection-banner" role="status">
          No connection to the server — reconnecting. What you see may be out of date.
        </p>
      )}

      {auth.status === 'checking' && <p className="subtitle">Checking the session…</p>}

      {auth.status === 'unreachable' && (
        <>
          <p className="subtitle">{auth.message}. The page cannot show anything until it answers.</p>
          <button type="button" className="retry" onClick={retry}>
            Try again
          </button>
        </>
      )}

      {auth.status === 'anonymous' && (
        <>
          <p className="subtitle">Sign in to upload documents.</p>
          <LoginForm
            onSignedIn={(user) => setAuth({ status: 'authenticated', user })}
            googleSignInEnabled={features.googleSignInEnabled}
          />
        </>
      )}

      {auth.status === 'authenticated' && (
        <>
          <p className="subtitle">
            Drop documents or images here. Each one is stored, then picked up by the AI pipeline
            and turned into a note.
          </p>
          <UploadDropZone
            onUploaded={() => setUploadCount((count) => count + 1)}
            maxSourceChars={features.maxSourceChars}
            maxUploadBytes={features.maxUploadBytes}
          />
          <AssetList reloadToken={uploadCount} maxSourceChars={features.maxSourceChars} />
          <NotesList />
        </>
      )}
    </main>
  )
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}

export default App
