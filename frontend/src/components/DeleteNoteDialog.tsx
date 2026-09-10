import { useEffect, useRef, useState } from 'react'
import { fetchBacklinks } from '../api/notes'
import type { Backlink, NoteSummary } from '../api/notes'

interface DeleteNoteDialogProps {
  /** The note to delete, or null when the dialog is closed. */
  note: NoteSummary | null
  busy: boolean
  onCancel: () => void
  onConfirm: (deleteSource: boolean) => void
}

type BacklinkState =
  | { status: 'loading' }
  | { status: 'ready'; backlinks: Backlink[] }
  | { status: 'error' }

/**
 * The confirmation for deleting a note. Says what it takes with it rather than asking whether
 * the user is sure: which notes lose a working link, and which file goes along.
 */
export function DeleteNoteDialog({ note, busy, onCancel, onConfirm }: DeleteNoteDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const [deleteSource, setDeleteSource] = useState(true)
  const [backlinks, setBacklinks] = useState<BacklinkState>({ status: 'loading' })

  // Modality only exists as an imperative call - the `open` attribute renders the dialog
  // inline, without the top layer, the backdrop or the focus trap.
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
        // The count is context, not a precondition: failing to fetch it must not block the
        // delete the user came here for.
        if (!cancelled) {
          setBacklinks({ status: 'error' })
        }
      })

    return () => {
      cancelled = true
    }
  }, [noteId])

  return (
    <dialog
      ref={dialogRef}
      className="confirm-dialog"
      aria-labelledby="delete-note-title"
      onCancel={(event) => {
        if (busy) {
          event.preventDefault()
          return
        }

        onCancel()
      }}
    >
      {note !== null && (
        <>
          <h3 id="delete-note-title">Delete “{note.title}”?</h3>

          <p className="confirm-line">
            It moves to the bin, where it can be restored or deleted permanently.
          </p>

          {backlinks.status === 'ready' && backlinks.backlinks.length > 0 && (
            <p className="confirm-line confirm-warn">
              {backlinks.backlinks.length === 1
                ? '1 note links here — its link will read as deleted: '
                : `${backlinks.backlinks.length} notes link here — their links will read as deleted: `}
              {backlinks.backlinks.map((backlink) => backlink.title).join(', ')}
            </p>
          )}

          {note.sourceAssetId !== null && (
            <label className="confirm-check">
              <input
                type="checkbox"
                checked={deleteSource}
                disabled={busy}
                onChange={(event) => setDeleteSource(event.target.checked)}
              />
              <span>
                Also delete the file{' '}
                {note.sourceFileName !== null ? `“${note.sourceFileName}”` : 'it was made from'}
              </span>
            </label>
          )}

          {note.sourceAssetId !== null && !deleteSource && (
            <p className="confirm-line confirm-hint">
              The file stays and goes back to unprocessed — you can run it through the pipeline
              again from the file list.
            </p>
          )}

          <div className="confirm-actions">
            <button type="button" onClick={onCancel} disabled={busy}>
              Cancel
            </button>
            <button
              type="button"
              className="confirm-danger"
              onClick={() => onConfirm(deleteSource)}
              disabled={busy}
            >
              {busy ? 'Deleting…' : 'Delete'}
            </button>
          </div>
        </>
      )}
    </dialog>
  )
}
