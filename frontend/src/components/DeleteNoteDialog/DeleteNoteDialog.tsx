import { useDeleteNoteDialog } from './useDeleteNoteDialog'
import type { NoteSummary } from '../../api/notes'
import './DeleteNoteDialog.css'

interface DeleteNoteDialogProps {
  /** The note to delete, or null when the dialog is closed. */
  note: NoteSummary | null
  busy: boolean
  onCancel: () => void
  onConfirm: (deleteSource: boolean) => void
}

// Names what the delete takes with it - which notes lose a link, which file goes along.
export function DeleteNoteDialog({ note, busy, onCancel, onConfirm }: DeleteNoteDialogProps) {
  const { dialogRef, deleteSource, setDeleteSource, backlinks } = useDeleteNoteDialog(note)

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
            <button type="button" className="btn btn-lg" onClick={onCancel} disabled={busy}>
              Cancel
            </button>
            <button
              type="button"
              className="btn btn-lg btn-danger"
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
