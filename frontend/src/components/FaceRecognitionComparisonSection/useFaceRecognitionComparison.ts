import { sameData } from '../../hooks/bufferedUpdates'
import { useVisibleAssets } from '../../hooks/useVisibleAssets'
import { useSectionRefresh } from '../../hooks/useSectionRefresh'
import { requestContext } from '../../diagnostics/diagnostics'
import type { RequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useEffect, useRef, useState } from 'react'
import { createRecognitionComparison, fetchRecognitionComparison, fetchRecognitionComparisonHistory } from '../../api/faceRecognitionComparisons'
import type { RecognitionComparisonHistory, RecognitionComparisonRun } from '../../api/faceRecognitionComparisons'
import { useResourceChanges } from '../../hooks/useResourceChanges'

export function useFaceRecognitionComparison() {
  const [page, setPage] = useState(0)
  const [chosenRunId, setChosenRunId] = useState('')
  type HistoryState = { page: number; data: RecognitionComparisonHistory } | null
  const refresh = useSectionRefresh<{ historyState: HistoryState; runState: RecognitionComparisonRun | null }>({ historyState: null, runState: null }, value => value.historyState !== null)
  const assets = useVisibleAssets(refresh.enabled)
  const { state: { historyState, runState }, receive, updateLocal, canLoad } = refresh
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
    if (!refresh.enabled) return
    let cancelled = false
    void fetchRecognitionComparisonHistory(page, changeContext.current).then(data => {
      if (cancelled) return
      if (changeContext.current.trigger === 'navigation') updateLocal(current => ({ ...current, historyState: { page, data } }))
      else receive(current => ({ ...current, historyState: { page, data } }))
    }).catch((cause: unknown) => { if (!cancelled) setError(cause instanceof Error ? cause.message : 'Could not load recognition comparisons') })
    return () => { cancelled = true }
  }, [page, revision, refresh.enabled, receive, updateLocal])
  useEffect(() => {
    if (!runId || !refresh.enabled) return
    let cancelled = false
    void fetchRecognitionComparison(runId, changeContext.current).then(data => {
      if (cancelled) return
      receive(current => ({ ...current, runState: data }))
      updateLocal(current => current.runState?.id === runId ? current : { ...current, runState: data })
    }).catch((cause: unknown) => { if (!cancelled) setError(cause instanceof Error ? cause.message : 'Could not load recognition comparison') })
    return () => { cancelled = true }
  }, [runId, revision, refresh.enabled, receive, updateLocal])
  useResourceChanges('photo-analysis', () => { if (canLoad()) advance('sse') })
  const pending = refresh.latest.runState?.status === 'Pending' || refresh.latest.runState?.status === 'Running'
  useEffect(() => {
    if (!pending || !refresh.enabled) return
    const timer = window.setInterval(() => advance('poll'), 3000)
    return () => window.clearInterval(timer)
  }, [pending, advance, refresh.enabled])

  return {
    refresh: { ...refresh, changed: !sameData(refresh.state, refresh.latest) }, assets, page, history, run, runId, error, busy, pending,
    chooseRun: (id: string) => { if (!busy) { changeContext.current = { source: 'FaceRecognitionComparison', trigger: 'navigation' }; setChosenRunId(id); setError(null) } },
    changePage: (next: number) => { if (!busy) { changeContext.current = { source: 'FaceRecognitionComparison', trigger: 'navigation' }; setPage(next); setChosenRunId('') } },
    reload: () => { setError(null); refresh.accept(); advance('manual') },
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
