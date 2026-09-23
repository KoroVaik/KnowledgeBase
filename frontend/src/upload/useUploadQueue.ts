import { useCallback, useEffect, useReducer, useRef } from 'react'
import { uploadAsset } from '../api/assets'
import { diagnosticId, record } from '../diagnostics/diagnostics'
import { classify } from './classify'
import type { UploadLimits, UploadProblem } from './classify'

export type ItemState =
  /** A warning the user has not answered yet: uploadable, but only on an explicit "anyway". */
  | { status: 'needs-decision' }
  /** The server would refuse these bytes, so there is nothing to offer but the reason. */
  | { status: 'blocked' }
  | { status: 'queued' }
  | { status: 'uploading'; progress: number | null }
  | { status: 'done' }
  | { status: 'failed'; message: string }

export interface QueuedItem {
  id: string
  file: File
  problem: UploadProblem | null
  state: ItemState
}

// A counter, not crypto.randomUUID(): that needs a secure context, and this app opens over
// plain http on a LAN address.
let lastId = 0

type Action =
  | { type: 'added'; items: QueuedItem[] }
  | { type: 'started'; id: string }
  | { type: 'progress'; id: string; fraction: number }
  | { type: 'creep'; id: string }
  | { type: 'succeeded'; id: string }
  | { type: 'failed'; id: string; message: string }
  | { type: 'dismissed'; ids: string[] }
  | { type: 'cleared' }

function reduce(items: QueuedItem[], action: Action): QueuedItem[] {
  switch (action.type) {
    case 'added':
      return [...items, ...action.items]
    case 'started':
      return patch(items, action.id, { status: 'uploading', progress: null })
    case 'progress':
      // Ignore a progress event that arrives after the request settled some other way.
      return items.map((item) =>
        item.id === action.id && item.state.status === 'uploading'
          ? { ...item, state: { status: 'uploading', progress: action.fraction } }
          : item,
      )
    case 'creep':
      // The confirm round-trip has no progress events of its own; creeping just under 100%
      // is honest, while a bar parked at 100% reads as "finished" while the row still waits.
      return items.map((item) => {
        if (item.id !== action.id || item.state.status !== 'uploading') {
          return item
        }

        const { progress } = item.state
        const next = progress === null ? 0.9 : Math.min(0.99, progress + 0.015)

        return { ...item, state: { status: 'uploading', progress: next } }
      })
    case 'succeeded':
      return patch(items, action.id, { status: 'done' })
    case 'failed':
      return patch(items, action.id, { status: 'failed', message: action.message })
    case 'dismissed':
      return items.filter((item) => !action.ids.includes(item.id))
    case 'cleared':
      return []
  }
}

function patch(items: QueuedItem[], id: string, state: ItemState): QueuedItem[] {
  return items.map((item) => (item.id === id ? { ...item, state } : item))
}

export function useUploadQueue(limits: UploadLimits, onUploaded: () => void) {
  const [items, dispatch] = useReducer(reduce, [])

  // Finalize-phase creep timers per row; any terminal state (success, failure, dismiss)
  // stops one, so a settled row never keeps ticking.
  const creepTimers = useRef(new Map<string, number>())

  const stopCreep = useCallback((id: string) => {
    const timer = creepTimers.current.get(id)
    if (timer !== undefined) {
      window.clearInterval(timer)
      creepTimers.current.delete(id)
    }
  }, [])

  const startCreep = useCallback((id: string) => {
    stopCreep(id)
    creepTimers.current.set(id, window.setInterval(() => dispatch({ type: 'creep', id }), 300))
  }, [stopCreep])

  useEffect(() => {
    const timers = creepTimers.current
    return () => {
      for (const timer of timers.values()) {
        window.clearInterval(timer)
      }
      timers.clear()
    }
  }, [])

  // In a ref so the callbacks below stay stable - rebuilding them mid-upload would be a bug.
  const onUploadedRef = useRef(onUploaded)
  useEffect(() => {
    onUploadedRef.current = onUploaded
  })

  const activeUploads = useRef(new Set<string>())
  const reloadTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  useEffect(() => () => { if (reloadTimer.current !== undefined) clearTimeout(reloadTimer.current) }, [])
  const uploadNow = useCallback(
    (targets: QueuedItem[]) => {
      const uploadBatchId = diagnosticId()
      record('upload.batch-started', { uploadBatchId, count: targets.length })
      for (const target of targets) {
        if (activeUploads.current.has(target.id) || target.state.status === 'done' || target.state.status === 'blocked') continue
        activeUploads.current.add(target.id)
        dispatch({ type: 'started', id: target.id })

        void uploadAsset(target.file, (phase, fraction) => {
          if (phase === 'transfer') {
            // The last 10% of the bar belongs to the confirm round-trip, which has no
            // progress events - the creep fills it once the bytes are in the bucket.
            dispatch({ type: 'progress', id: target.id, fraction: fraction * 0.9 })
          } else {
            startCreep(target.id)
          }
        }, { uploadBatchId })
          .then(() => {
            stopCreep(target.id)
            dispatch({ type: 'succeeded', id: target.id })
            if (reloadTimer.current === undefined) reloadTimer.current = setTimeout(() => {
              reloadTimer.current = undefined
              onUploadedRef.current()
            }, 20)
          })
          .catch((error: unknown) => {
            stopCreep(target.id)
            dispatch({ type: 'failed', id: target.id, message: messageOf(error) })
          }).finally(() => activeUploads.current.delete(target.id))
      }
    },
    [startCreep, stopCreep],
  )

  const { maxUploadBytes, maxSourceChars } = limits

  const add = useCallback(
    (files: File[]) => {
      const added = files.map((file) => toItem(file, { maxUploadBytes, maxSourceChars }))

      dispatch({ type: 'added', items: added })
      uploadNow(added.filter((item) => item.state.status === 'queued'))
    },
    [maxUploadBytes, maxSourceChars, uploadNow],
  )

  const dismiss = useCallback(
    (ids: string[]) => {
      for (const id of ids) {
        stopCreep(id)
      }
      dispatch({ type: 'dismissed', ids })
    },
    [stopCreep],
  )

  const reset = useCallback(() => {
    for (const timer of creepTimers.current.values()) {
      window.clearInterval(timer)
    }
    creepTimers.current.clear()
    dispatch({ type: 'cleared' })
  }, [])

  return { items, add, uploadNow, dismiss, reset }
}

function toItem(file: File, limits: UploadLimits): QueuedItem {
  const problem = classify(file, limits)

  return {
    id: `f${++lastId}`,
    file,
    problem,
    state:
      problem === null
        ? { status: 'queued' }
        : problem.kind === 'warning'
          ? { status: 'needs-decision' }
          : { status: 'blocked' },
  }
}

/** A row the dialog cannot resolve on its own - the user has to upload it, retry it or drop it. */
export function needsAttention(item: QueuedItem): boolean {
  return (
    item.state.status === 'needs-decision' ||
    item.state.status === 'blocked' ||
    item.state.status === 'failed'
  )
}

/** Bytes are on the wire. Closing now would abort them, so ESC is refused while this holds. */
export function isBusy(items: QueuedItem[]): boolean {
  return items.some((item) => item.state.status === 'queued' || item.state.status === 'uploading')
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
