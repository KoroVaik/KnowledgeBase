import { useEffect, useState } from 'react'
import { fetchCurrentUser, logout } from './api/auth'
import type { CurrentUser } from './api/auth'
import { FileUploadForm } from './components/FileUploadForm'
import { LoginForm } from './components/LoginForm'
import './App.css'

type AuthState =
  | { status: 'checking' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; user: CurrentUser }

function App() {
  const [auth, setAuth] = useState<AuthState>({ status: 'checking' })

  useEffect(() => {
    let cancelled = false

    void fetchCurrentUser()
      .then((user) => {
        if (!cancelled) {
          setAuth(user === null ? { status: 'anonymous' } : { status: 'authenticated', user })
        }
      })
      .catch(() => {
        if (!cancelled) {
          setAuth({ status: 'anonymous' })
        }
      })

    return () => {
      cancelled = true
    }
  }, [])

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

      {auth.status === 'checking' && <p className="subtitle">Checking the session…</p>}

      {auth.status === 'anonymous' && (
        <>
          <p className="subtitle">Sign in to upload documents.</p>
          <LoginForm onSignedIn={(user) => setAuth({ status: 'authenticated', user })} />
        </>
      )}

      {auth.status === 'authenticated' && (
        <>
          <p className="subtitle">
            Upload a file to <code>backend/data/assets</code>. AI processing is not wired up yet.
          </p>
          <FileUploadForm />
        </>
      )}
    </main>
  )
}

export default App
