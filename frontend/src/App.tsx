import { AssetList } from './components/AssetList/AssetList'
import { LoginForm } from './components/LoginForm/LoginForm'
import { UploadDropZone } from './components/UploadDropZone'
import { NotesList } from './components/NotesList/NotesList'
import { TagsSection } from './components/TagsSection/TagsSection'
import { JobsSection } from './components/JobsSection/JobsSection'
import { useAppAuth } from './hooks/useAppAuth'
import './App.css'

function App() {
  const { auth, features, connection, uploadCount, retry, handleSignOut, markSignedIn, bumpUploadCount } =
    useAppAuth()

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
        // role="status", not "alert": not a response to a user action.
        <p className="connection-banner" role="status">
          No connection to the server — reconnecting. What you see may be out of date.
        </p>
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
