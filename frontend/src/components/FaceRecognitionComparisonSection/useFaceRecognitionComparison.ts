import { requestContext } from '../../diagnostics/diagnostics'
import type { RequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useEffect, useRef, useState } from 'react'
import { createRecognitionComparison, fetchRecognitionComparison, fetchRecognitionComparisonHistory } from '../../api/faceRecognitionComparisons'
import type { RecognitionComparisonHistory, RecognitionComparisonRun } from '../../api/faceRecognitionComparisons'
import { useResourceChanges } from '../../hooks/useResourceChanges'

export function useFaceRecognitionComparison() {
  const [page, setPage] = useState(0)
  const [chosenRunId, setChosenRunId] = useState('')
  const [historyState, setHistory] = useState<{ page: number; data: RecognitionComparisonHistory } | null>(null)
  const [runState, setRun] = useState<RecognitionComparisonRun | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [revision, setRevision] = useState(0)
  const changeContext = useRef<RequestContext>({ source: 'FaceRecognitionComparison', trigger: 'mount' })
  const advance = useCallback((trigger = 'action') => {
    changeContext.current = requestContext('FaceRecognitionComparison', trigger)
    setRevision(value => value + 1)
  }, [])
  const history = historyState?.page === page ? historyState.data : null
  const runId = chosenRunId || history?.runs[0]?.id || ''
  const run = runState?.id === runId ? runState : null

  useEffect(() => {
    let cancelled = false
    void fetchRecognitionComparisonHistory(page, changeContext.current).then(data => {
      if (!cancelled) setHistory({ page, data })
    }).catch((cause: unknown) => { if (!cancelled) setError(cause instanceof Error ? cause.message : 'Could not load recognition comparisons') })
    return () => { cancelled = true }
  }, [page, revision])
  useEffect(() => {
    if (!runId) return
    let cancelled = false
    void fetchRecognitionComparison(runId, changeContext.current).then(data => {
      if (!cancelled) setRun(data)
    }).catch((cause: unknown) => { if (!cancelled) setError(cause instanceof Error ? cause.message : 'Could not load recognition comparison') })
    return () => { cancelled = true }
  }, [runId, revision])
  useResourceChanges('photo-analysis', () => advance())
  const pending = run?.status === 'Pending' || run?.status === 'Running'
  useEffect(() => {
    if (!pending) return
    const timer = window.setInterval(() => advance('poll'), 3000)
    return () => window.clearInterval(timer)
  }, [pending, advance])

  return {
    page, history, run, runId, error, busy, pending,
    chooseRun: (id: string) => { if (!busy) { changeContext.current = { source: 'FaceRecognitionComparison', trigger: 'navigation' }; setChosenRunId(id); setError(null) } },
    changePage: (next: number) => { if (!busy) { changeContext.current = { source: 'FaceRecognitionComparison', trigger: 'navigation' }; setPage(next); setChosenRunId('') } },
    reload: () => { setError(null); advance() },
    compare: async () => {
      if (busy) return
      setBusy(true); setError(null)
      try {
        const created = await createRecognitionComparison()
        setPage(0)
        setChosenRunId(created.id ?? '')
        advance()
      } catch (cause) {
        setError(cause instanceof Error ? cause.message : 'Could not start recognition comparison')
      } finally { setBusy(false) }
    },
  }
}
