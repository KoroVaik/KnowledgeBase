import { AssetList } from './components/AssetList/AssetList'
import { LoginForm } from './components/LoginForm/LoginForm'
import { UploadDropZone } from './components/UploadDropZone'
import { NotesList } from './components/NotesList/NotesList'
import { TagsSection } from './components/TagsSection/TagsSection'
import { JobsSection } from './components/JobsSection/JobsSection'
import { useAppAuth } from './hooks/useAppAuth'
import './App.css'

function App() {
  const {
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
  } = useAppAuth()

  return (
    <main className="app">
      <header className="app-header">
        <h1>Knowledge Base</h1>
        {auth.status === 'authenticated' && (
          <button type="button" className="btn btn-md" onClick={() => void handleSignOut()}>
            Sign out
          </button>
        )}
      </header>

      {connection === 'offline' && (
        <div className="connection-banner">
          {/* Not an alert: this is not a response to a user action. */}
          <p className="connection-banner-text" role="status">
            Live updates are temporarily unavailable — reconnecting.
          </p>
          {reconnectInSeconds !== null && (
            <span className="connection-retry-countdown" aria-live="off">
              Next attempt in {reconnectInSeconds} s.
            </span>
          )}
          <button type="button" className="btn btn-sm" onClick={reconnect}>
            Reconnect now
          </button>
        </div>
      )}

      {auth.status === 'checking' && <p className="subtitle">Checking the session…</p>}

      {auth.status === 'unreachable' && (
        <>
          <p className="subtitle">{auth.message}. The page cannot show anything until it answers.</p>
          <button type="button" className="btn btn-lg btn-primary" onClick={retry}>
            Try again
          </button>
        </>
      )}

      {auth.status === 'anonymous' && (
        <>
          {auth.message && <p className="auth-message" role="status">{auth.message}</p>}
          <p className="subtitle">Sign in to upload documents.</p>
          <LoginForm onSignedIn={markSignedIn} googleSignInEnabled={features.googleSignInEnabled} />
        </>
      )}

      {auth.status === 'authenticated' && (
        <>
          <p className="subtitle">
            Drop documents or images here. Each one is stored, then picked up by the AI pipeline
            and turned into a note.
          </p>
          <UploadDropZone
            onUploaded={bumpUploadCount}
            maxSourceChars={features.maxSourceChars}
            maxUploadBytes={features.maxUploadBytes}
          />
          <AssetList reloadToken={uploadCount} maxSourceChars={features.maxSourceChars} />
          <NotesList />
          <TagsSection />
          <JobsSection />
        </>
      )}
    </main>
  )
}

export default App
