import { useCallback, useEffect, useReducer, useRef } from 'react'
import { uploadAsset } from '../api/assets'
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

  // In a ref so the callbacks below stay stable - rebuilding them mid-upload would be a bug.
  const onUploadedRef = useRef(onUploaded)
  useEffect(() => {
    onUploadedRef.current = onUploaded
  })

  // Every file at once, no pool (single-user, tens of files). Launched here, not from an
  // effect: StrictMode runs effects twice in dev and would upload each file a second time.
  const uploadNow = useCallback((targets: QueuedItem[]) => {
    for (const target of targets) {
      dispatch({ type: 'started', id: target.id })

      void uploadAsset(target.file, (fraction) => {
        dispatch({ type: 'progress', id: target.id, fraction })
      })
        .then(() => {
          dispatch({ type: 'succeeded', id: target.id })
          onUploadedRef.current()
        })
        .catch((error: unknown) => {
          dispatch({ type: 'failed', id: target.id, message: messageOf(error) })
        })
    }
  }, [])

  const { maxUploadBytes, maxSourceChars } = limits

  const add = useCallback(
    (files: File[]) => {
      const added = files.map((file) => toItem(file, { maxUploadBytes, maxSourceChars }))

      dispatch({ type: 'added', items: added })
      uploadNow(added.filter((item) => item.state.status === 'queued'))
    },
    [maxUploadBytes, maxSourceChars, uploadNow],
  )

  const dismiss = useCallback((ids: string[]) => {
    dispatch({ type: 'dismissed', ids })
  }, [])

  const reset = useCallback(() => {
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
