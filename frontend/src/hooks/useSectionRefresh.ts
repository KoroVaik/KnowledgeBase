import { useCallback, useEffect, useRef, useState } from 'react'
import type { SetStateAction } from 'react'
import { collectItemKeys, hasExistingChanges, insertNewItems, itemKey, mergeAccepted } from './bufferedUpdates'
import type { ListUpdates } from './bufferedUpdates'
import { captureRequestContext, withRequestContext } from '../diagnostics/diagnostics'

export function useSectionRefresh<T>(initial: T, ready: (value: T) => boolean, collapsed = false) {
  const [snapshot, setSnapshot] = useState({ shown: initial, latest: initial })
  const [node, setNode] = useState<HTMLElement | null>(null)
  const [visible, setVisible] = useState(false)
  const [tabVisible, setTabVisible] = useState(document.visibilityState === 'visible')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const engaged = useRef(false)
  const drafts = useRef(new Set<string>())
  const active = useRef(false)
  const replaceOnArrival = useRef(false)
  const manual = useRef(false)
  const accepted = useRef(new Set<string>())
  const stale = useRef(true)
  const loadedAt = useRef(0)
  const readyRef = useRef(ready)
  useEffect(() => { readyRef.current = ready }, [ready])

  useEffect(() => {
    if (!node) return
    let timer: ReturnType<typeof setTimeout> | undefined
    const observer = new IntersectionObserver(entries => {
      clearTimeout(timer)
      if (entries[0].isIntersecting) timer = setTimeout(() => setVisible(true), 180)
      else { active.current = false; setVisible(false) }
    })
    observer.observe(node)
    return () => { observer.disconnect(); clearTimeout(timer) }
  }, [node])
  useEffect(() => {
    const change = () => {
      if (document.visibilityState !== 'visible') active.current = false
      setTabVisible(document.visibilityState === 'visible')
    }
    document.addEventListener('visibilitychange', change)
    return () => document.removeEventListener('visibilitychange', change)
  }, [])

  const enabled = visible && tabVisible && !collapsed
  useEffect(() => {
    active.current = enabled
    if (enabled) replaceOnArrival.current = drafts.current.size === 0
    else engaged.current = false
  }, [enabled])

  const receive = useCallback((value: SetStateAction<T>, acceptedIds: readonly string[] = []) => {
    const replace = manual.current || (active.current && replaceOnArrival.current && !engaged.current && drafts.current.size === 0)
    const ids = new Set([...accepted.current, ...acceptedIds])
    accepted.current.clear()
    replaceOnArrival.current = false
    manual.current = false
    stale.current = false
    loadedAt.current = Date.now()
    setSnapshot(current => {
      const next = typeof value === 'function' ? (value as (previous: T) => T)(current.latest) : value
      const shown = !readyRef.current(current.shown) || replace ? next
        : ids.size ? mergeAccepted(current.shown, next, ids) : current.shown
      return { shown, latest: next }
    })
  }, [])

  const updateLocal = useCallback((update: (value: T) => T) => {
    setSnapshot(current => ({ shown: update(current.shown), latest: update(current.latest) }))
  }, [])
  const accept = useCallback(() => {
    setSnapshot(current => ({ shown: current.latest, latest: current.latest }))
    setError(null)
  }, [])
  const canLoad = useCallback((acceptedIds: readonly string[] = []) => {
    acceptedIds.forEach(id => accepted.current.add(id))
    if (manual.current || acceptedIds.length > 0) return true
    if (!active.current) { stale.current = true; return false }
    if (captureRequestContext().trigger === 'visibility' && !stale.current && Date.now() - loadedAt.current <= 15000) {
      replaceOnArrival.current = false
      return false
    }
    return true
  }, [])
  const reload = useCallback(async (load: () => unknown) => {
    manual.current = true
    setLoading(true)
    setError(null)
    try { await withRequestContext({ trigger: 'manual' }, load) }
    finally { manual.current = false; setLoading(false) }
  }, [])
  const protect = useCallback(() => { engaged.current = true; replaceOnArrival.current = false }, [])
  const registerDraft = useCallback((id: string, dirty: boolean) => {
    if (dirty) drafts.current.add(id)
    else drafts.current.delete(id)
  }, [])

  function additions<U>(shown: readonly U[], latest: readonly U[], key: (item: U) => string | null = itemKey): ListUpdates {
    const known = new Set(shown.map(key))
    const allKnown = collectItemKeys(snapshot.shown)
    const newItems = latest.filter(item => !known.has(key(item)) && ![...collectItemKeys(item)].some(id => allKnown.has(id)))
    const ids = new Set(newItems.map(key).filter((id): id is string => id !== null))
    const merged = insertNewItems(shown, latest.filter(item => known.has(key(item)) || ids.has(key(item) ?? '')), key)
    return {
      count: ids.size,
      revealCount: merged.reduce((count, item, index) => ids.has(key(item) ?? '') ? index + 1 : count, 0),
      show: () => {
        protect()
        setSnapshot(current => ({ ...current, shown: mergeAccepted(current.shown, current.latest, ids, true) }))
      },
    }
  }

  return {
    state: snapshot.shown, latest: snapshot.latest, receive, updateLocal, accept, additions, registerDraft,
    enabled, canLoad, reload, error, setError, loading, setLoading,
    changed: ready(snapshot.shown) && hasExistingChanges(snapshot.shown, snapshot.latest),
    bind: { ref: setNode, onChangeCapture: protect, onInputCapture: protect, onKeyDownCapture: protect,
      onPointerDownCapture: (event: React.PointerEvent<HTMLElement>) => {
        const target = event.target as HTMLElement
        if (!target.closest('.section-toggle, .subsection-toggle, .section-refresh')) protect()
      } },
  }
}

export function useVisibleReload(enabled: boolean, reload: () => unknown, pollMs?: number) {
  const latest = useRef(reload)
  useEffect(() => { latest.current = reload }, [reload])
  useEffect(() => {
    if (!enabled) return
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | undefined
    const load = async (trigger: string) => {
      await withRequestContext({ trigger }, () => latest.current())
      if (!stopped && pollMs) timer = setTimeout(() => void load('poll'), pollMs)
    }
    void load(pollMs ? 'poll' : 'visibility')
    return () => { stopped = true; clearTimeout(timer) }
  }, [enabled, pollMs])
}
