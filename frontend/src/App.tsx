import { useEffect, useRef, useState } from 'react'
import { fetchCurrentUser, logout } from './api/auth'
import type { CurrentUser } from './api/auth'
import { fetchFeatures } from './api/features'
import type { FeatureFlags } from './api/features'
import { AssetList } from './components/AssetList'
import { FileUploadForm } from './components/FileUploadForm'
import { LoginForm } from './components/LoginForm'
import { useConnectionStatus } from './hooks/useConnectionStatus'
import './App.css'

type AuthState =
  | { status: 'checking' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; user: CurrentUser }
  | { status: 'unreachable'; message: string }

function App() {
  const [auth, setAuth] = useState<AuthState>({ status: 'checking' })
  // The form and the list are siblings, so what they share lives in their parent.
  const [uploadCount, setUploadCount] = useState(0)
  // Bumped by "Try again": the load lives in an effect, and a new value is how a button
  // outside it asks for another run.
  const [attempt, setAttempt] = useState(0)
  // Until the flags arrive, assume the feature is off: showing a working control that
  // the server then refuses is worse than showing a disabled one for a moment.
  const [features, setFeatures] = useState<FeatureFlags>({
    uploadEnabled: false,
    downloadEnabled: false,
    googleSignInEnabled: false,
    directAssetAccessEnabled: false,
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
          // Not `anonymous`: the one case that means "nobody is signed in" is a 401, and
          // fetchCurrentUser answers that with null. Everything else leaves the session
          // genuinely unknown, and the sign-in form would be a claim we cannot make - with
          // the API down there is nothing to sign in to.
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

  // The session cookie can expire and a flag can be flipped while the tab sits in the
  // background. When the change stream comes back after being down, re-check both - but
  // quietly: unlike the first load this must not blank the page to "checking", and a failure
  // is left to the connection banner rather than replacing the view with an error.
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

  // The state is reset here rather than inside the effect: the effect synchronises with the
  // server, and the click is what actually put the page back into "checking".
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
        // role="status", not "alert": this is not the answer to something the user just did,
        // and a screen reader should finish what it is saying first.
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
            Upload a file to <code>backend/data/assets</code>. AI processing is not wired up yet.
          </p>
          <FileUploadForm
            onUploaded={() => setUploadCount((count) => count + 1)}
            uploadEnabled={features.uploadEnabled}
            directUpload={features.directAssetAccessEnabled}
          />
          <AssetList
            reloadToken={uploadCount}
            downloadEnabled={features.downloadEnabled}
            directDownload={features.directAssetAccessEnabled}
          />
        </>
      )}
    </main>
  )
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}

export default App
