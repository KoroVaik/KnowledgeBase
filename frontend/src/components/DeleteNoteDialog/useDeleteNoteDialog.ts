import { useEffect, useRef, useState } from 'react'
import { fetchBacklinks } from '../../api/notes'
import type { Backlink, NoteSummary } from '../../api/notes'

export type BacklinkState =
  | { status: 'loading' }
  | { status: 'ready'; backlinks: Backlink[] }
  | { status: 'error' }

/** State behind DeleteNoteDialog: the dialog's own open/close (an imperative DOM call, not
 *  a prop), the "also delete the file" checkbox, and the backlink count fetched per note. */
export function useDeleteNoteDialog(note: NoteSummary | null) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const [deleteSource, setDeleteSource] = useState(true)
  const [backlinks, setBacklinks] = useState<BacklinkState>({ status: 'loading' })

  // Modality only exists as an imperative call - React has to reach into the DOM here.
  useEffect(() => {
    const dialog = dialogRef.current

    if (dialog === null) {
      return
    }

    if (note !== null && !dialog.open) {
      dialog.showModal()
    } else if (note === null && dialog.open) {
      dialog.close()
    }
  }, [note])

  const noteId = note?.id ?? null

  useEffect(() => {
    if (noteId === null) {
      return
    }

    let cancelled = false

    void fetchBacklinks(noteId)
      .then((found) => {
        if (!cancelled) {
          setBacklinks({ status: 'ready', backlinks: found })
        }
      })
      .catch(() => {
        // The count is context, not a precondition - a failed fetch must not block the delete.
        if (!cancelled) {
          setBacklinks({ status: 'error' })
        }
      })

    return () => {
      cancelled = true
    }
  }, [noteId])

  return { dialogRef, deleteSource, setDeleteSource, backlinks }
}
