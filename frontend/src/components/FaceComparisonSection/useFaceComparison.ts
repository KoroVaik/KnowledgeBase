import { sameData } from '../../hooks/bufferedUpdates'
import { useVisibleAssets } from '../../hooks/useVisibleAssets'
import { useSectionRefresh } from '../../hooks/useSectionRefresh'
import type { SetStateAction } from 'react'
import { requestContext } from '../../diagnostics/diagnostics'
import type { RequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useEffect, useRef, useState } from 'react'
import { createAllComparisons, fetchComparison, fetchComparisonHistory, reviewDetection, reviewComparisonDetections, setComparisonMissedFaces, setComparisonReviewed, setComparisonSkipped, setMissedFaces } from '../../api/faceComparisons'
import type { ComparisonHistory, ComparisonRun } from '../../api/faceComparisons'
import { useResourceChanges } from '../../hooks/useResourceChanges'

export function useFaceComparison() {
  const [page, setPage] = useState(0)
  const [chosenRunId, setChosenRunId] = useState('')
  type HistoryState = { key: string; data: ComparisonHistory } | null
  const refresh = useSectionRefresh<{ historyState: HistoryState; runState: ComparisonRun | null }>({ historyState: null, runState: null }, value => value.historyState !== null)
  const assets = useVisibleAssets(refresh.enabled)
  const { state: { historyState, runState }, receive, updateLocal, canLoad } = refresh
  const setHistory = (value: HistoryState) => updateLocal(current => ({ ...current, historyState: value }))
  const setRun = (value: SetStateAction<ComparisonRun | null>) => updateLocal(current => ({ ...current, runState: typeof value === 'function' ? value(current.runState) : value }))
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [revision, setRevision] = useState(0)
  const changeContext = useRef<RequestContext>({ source: 'FaceComparison', trigger: 'mount' })
  const advance = useCallback((trigger = 'action') => {
    changeContext.current = requestContext('FaceComparison', trigger)
    setRevision(value => value + 1)
  }, [])
  const [showReviewed, setShowReviewed] = useState(false)
  const pendingNavigation = useRef<-1 | 1 | null>(null)
  const historyKey = `${page}:${showReviewed}`
  const history = historyState?.key === historyKey ? historyState.data : null
  const runId = chosenRunId || history?.runs[0]?.id || ''
  const run = runState?.id === runId ? runState : null

  useEffect(() => {
    if (!refresh.enabled) return
    let cancelled = false
    void fetchComparisonHistory('', page, showReviewed, changeContext.current).then(data => {
      if (cancelled) return
      const value = { key: `${page}:${showReviewed}`, data }
      if (changeContext.current.trigger === 'filter' || changeContext.current.trigger === 'navigation') updateLocal(current => ({ ...current, historyState: value }))
      else receive(current => ({ ...current, historyState: value }))
      if (pendingNavigation.current !== null) {
        setChosenRunId(pendingNavigation.current < 0 ? data.runs.at(-1)?.id ?? '' : data.runs[0]?.id ?? '')
        pendingNavigation.current = null
      }
    }).catch((cause: unknown) => { if (!cancelled) setError(cause instanceof Error ? cause.message : 'Could not load comparisons') })
    return () => { cancelled = true }
  }, [page, revision, showReviewed, refresh.enabled, receive, updateLocal])
  useEffect(() => {
    if (!runId || !refresh.enabled) return
    let cancelled = false
    void fetchComparison(runId, changeContext.current).then(data => {
      if (cancelled) return
      receive(current => ({ ...current, runState: data }))
      updateLocal(current => current.runState?.id === runId ? current : { ...current, runState: data })
    }).catch((cause: unknown) => { if (!cancelled) setError(cause instanceof Error ? cause.message : 'Could not load comparison') })
    return () => { cancelled = true }
  }, [runId, revision, refresh.enabled, receive, updateLocal])
  useResourceChanges('photo-analysis', () => { if (canLoad()) advance('sse') })
  const pending = (refresh.latest.historyState?.data.inProgressPhotoCount ?? 0) > 0
  useEffect(() => {
    if (!pending || !refresh.enabled) return
    const timer = window.setInterval(() => advance('poll'), 3000)
    return () => window.clearInterval(timer)
  }, [pending, advance, refresh.enabled])

  async function save(action: () => Promise<unknown>) {
    if (busy) return
    setBusy(true)
    setError(null)
    try { await action(); advance() }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Could not save comparison') }
    finally { setBusy(false) }
  }

  return {
    refresh: { ...refresh, changed: !sameData(refresh.state, refresh.latest) }, assets, page, history, run, runId, error, busy, pending, showReviewed,
    chooseRun: (id: string) => { if (!busy) { changeContext.current = { source: 'FaceComparison', trigger: 'navigation' }; setChosenRunId(id); setError(null) } },
    changePage: (next: number) => { if (!busy) { changeContext.current = { source: 'FaceComparison', trigger: 'navigation' }; setPage(next); setChosenRunId(''); pendingNavigation.current = null } },
    setShowReviewed: (value: boolean) => { if (!busy) { changeContext.current = { source: 'FaceComparison', trigger: 'filter' }; setShowReviewed(value); setPage(0); setChosenRunId(''); pendingNavigation.current = null } },
    navigate: (direction: -1 | 1) => void (async () => {
      if (busy || history === null || run === null) return
      const index = history.runs.findIndex(item => item.id === runId)
      if (index < 0) return
      const shouldMarkReviewed = run.status === 'Done' && !run.isSkipped && run.reviewedAtUtc === null
        && run.results.every(result => result.error === null && result.completedAtUtc !== null)
      setBusy(true)
      setError(null)
      try {
        if (shouldMarkReviewed) {
          await setComparisonReviewed(run.id, true)
          setRun(current => current?.id === run.id ? { ...current, reviewedAtUtc: new Date().toISOString() } : current)
        }
        const currentPage = shouldMarkReviewed ? await fetchComparisonHistory('', page, showReviewed) : history
        if (shouldMarkReviewed) setHistory({ key: historyKey, data: currentPage })
        const removed = shouldMarkReviewed && !showReviewed
        const targetIndex = direction < 0 ? index - 1 : index + (removed ? 0 : 1)
        if (currentPage.runs[targetIndex]) {
          setChosenRunId(currentPage.runs[targetIndex].id)
        } else if (direction < 0 && page > 0) {
          const previous = await fetchComparisonHistory('', page - 1, showReviewed)
          setPage(page - 1)
          setHistory({ key: `${page - 1}:${showReviewed}`, data: previous })
          setChosenRunId(previous.runs.at(-1)?.id ?? '')
        } else if (direction > 0 && currentPage.hasMore) {
          const next = await fetchComparisonHistory('', page + 1, showReviewed)
          setPage(page + 1)
          setHistory({ key: `${page + 1}:${showReviewed}`, data: next })
          setChosenRunId(next.runs[0]?.id ?? '')
        } else if (removed) {
          setPage(0)
          const first = await fetchComparisonHistory('', 0, showReviewed)
          setHistory({ key: `0:${showReviewed}`, data: first })
          setChosenRunId(first.runs[0]?.id ?? '')
        }
        if (shouldMarkReviewed) advance()
      } catch (cause) {
        setError(cause instanceof Error ? cause.message : 'Could not finish reviewing this photo')
      } finally {
        setBusy(false)
      }
    })(),
    hasPrevious: history !== null && (history.runs.findIndex(item => item.id === runId) > 0 || page > 0),
    hasNext: history !== null && (() => { const index = history.runs.findIndex(item => item.id === runId); return index >= 0 && (index < history.runs.length - 1 || history.hasMore || (run?.status === 'Done' && !run.isSkipped && run.reviewedAtUtc === null)) })(),
    reload: () => { setError(null); refresh.accept(); advance('manual') },
    compareAll: () => void save(createAllComparisons),
    review: (id: string, isFace: boolean | null) => void save(async () => {
      await reviewDetection(id, isFace)
      setRun(current => current ? { ...current, results: current.results.map(result => ({ ...result,
        detections: result.detections.map(face => face.id === id ? { ...face, isFace } : face) })) } : current)
    }),
    missed: (id: string, count: number | null) => void save(async () => {
      await setMissedFaces(id, count)
      setRun(current => current ? { ...current, results: current.results.map(result => result.id === id ? { ...result, missedFaces: count } : result) } : current)
    }),
    reviewMany: (runId: string, reviews: { id: string; isFace: boolean }[]) => void save(async () => {
      await reviewComparisonDetections(runId, reviews)
      const decisions = new Map(reviews.map(item => [item.id, item.isFace]))
      setRun(current => current?.id !== runId ? current : { ...current, results: current.results.map(result => ({ ...result,
        detections: result.detections.map(face => decisions.has(face.id) ? { ...face, isFace: decisions.get(face.id)! } : face) })) })
    }),
    missedForAll: (runId: string, count: number | null) => void save(async () => {
      await setComparisonMissedFaces(runId, count)
      setRun(current => current?.id !== runId ? current : { ...current, results: current.results.map(result => ({ ...result, missedFaces: count })) })
    }),
    skipped: (runId: string, isSkipped: boolean) => void save(async () => {
      await setComparisonSkipped(runId, isSkipped)
      setRun(current => current?.id !== runId ? current : { ...current, isSkipped })
    }),
  }
}
