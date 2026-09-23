import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { initializeDiagnostics } from './diagnostics/diagnostics'
import { DiagnosticBoundary } from './diagnostics/DiagnosticBoundary'

initializeDiagnostics()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <DiagnosticBoundary><App /></DiagnosticBoundary>
  </StrictMode>,
)
