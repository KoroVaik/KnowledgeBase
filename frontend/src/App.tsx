import { FileUploadForm } from './components/FileUploadForm'
import './App.css'

function App() {
  return (
    <main className="app">
      <h1>Knowledge Base</h1>
      <p className="subtitle">
        Upload a file to <code>backend/data/assets</code>. AI processing is not wired up yet.
      </p>
      <FileUploadForm />
    </main>
  )
}

export default App
