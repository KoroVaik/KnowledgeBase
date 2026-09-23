import { Component } from 'react'
import type { ErrorInfo, ReactNode } from 'react'
import { exportDiagnostics, record } from './diagnostics'

function downloadDiagnostics() {
  const url = URL.createObjectURL(new Blob([exportDiagnostics()], { type: 'application/json' }))
  const link = document.createElement('a')
  link.href = url
  link.download = 'knowledgebase-diagnostics.json'
  link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export class DiagnosticBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  componentDidCatch(error: Error, info: ErrorInfo) {
    record('react.error', { errorType: error.name, message: error.message, stack: info.componentStack ?? error.stack }, 'error')
  }
  render() {
    if (this.state.failed) return <main role="alert">
      <h1>Something went wrong</h1>
      <p>You can save a diagnostic report before reloading.</p>
      <button type="button" onClick={downloadDiagnostics}>Save diagnostic report</button>
      <button type="button" onClick={() => window.location.reload()}>Reload</button>
    </main>
    return this.props.children
  }
}
