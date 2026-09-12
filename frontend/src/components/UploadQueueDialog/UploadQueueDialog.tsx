import { formatSize } from '../../format'
import { useUploadQueueDialog } from './useUploadQueueDialog'
import type { QueuedItem } from '../../upload/useUploadQueue'
import './UploadQueueDialog.css'

interface UploadQueueDialogProps {
  open: boolean
  items: QueuedItem[]
  onUploadNow: (items: QueuedItem[]) => void
  onDismiss: (ids: string[]) => void
  onClose: () => void
}

// Native <dialog> + showModal(): top layer (no ancestor clips it), ::backdrop, inert page,
// focus trap - all free. See docs/frontend.md.
export function UploadQueueDialog({
  open,
  items,
  onUploadNow,
  onDismiss,
  onClose,
}: UploadQueueDialogProps) {
  const { dialogRef, busy, warnings, attention, done } = useUploadQueueDialog(open, items)

  return (
    <dialog
      ref={dialogRef}
      className="queue-dialog"
      aria-labelledby="queue-title"
      // Refuse Escape mid-upload via the cancelable `cancel` event. Backdrop clicks need no
      // guard - <dialog> does not close on them.
      onCancel={(event) => {
        if (busy) {
          event.preventDefault()
        }
      }}
      onClose={onClose}
    >
      <h2 id="queue-title">Upload</h2>

      <p className="queue-summary" role="status">
        {done} of {items.length} uploaded
        {attention.length > 0 && ` · ${attention.length} need your attention`}
      </p>

      <div className="queue-scroll">
        <table className="queue-table">
          <thead>
            <tr>
              <th scope="col">File</th>
              <th scope="col">Size</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.id}>
                <td className="queue-name">{item.file.name}</td>
                <td className="queue-size">{formatSize(item.file.size)}</td>
                <td className="queue-status">
                  <StatusCell item={item} onUploadNow={onUploadNow} onDismiss={onDismiss} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="queue-footer">
        {warnings.length > 0 && (
          <button
            type="button"
            className="btn btn-lg btn-primary"
            onClick={() => onUploadNow(warnings)}
          >
            Upload all anyway
          </button>
        )}

        {attention.length > 0 && (
          <button
            type="button"
            className="btn btn-lg"
            onClick={() => onDismiss(attention.map((item) => item.id))}
          >
            Dismiss all
          </button>
        )}

        {/* close(), so every exit path arrives through the same `close` event. */}
        <button type="button" className="btn btn-lg" onClick={() => dialogRef.current?.close()} disabled={busy}>
          Close
        </button>
      </div>
    </dialog>
  )
}

interface StatusCellProps {
  item: QueuedItem
  onUploadNow: (items: QueuedItem[]) => void
  onDismiss: (ids: string[]) => void
}

function StatusCell({ item, onUploadNow, onDismiss }: StatusCellProps) {
  switch (item.state.status) {
    case 'queued':
      return <span className="queue-note">Waiting…</span>

    case 'uploading': {
      const { progress } = item.state

      return (
        <>
          {/* No `value` while unknown - that is the native indeterminate look, not a hard zero. */}
          <progress
            className="upload-progress"
            max={1}
            value={progress ?? undefined}
            aria-label={`Uploading ${item.file.name}`}
          />
          <span className="queue-note">
            {progress === null ? 'Uploading…' : `${Math.round(progress * 100)}%`}
          </span>
        </>
      )
    }

    case 'done':
      return <span className="queue-ok">Uploaded</span>

    case 'failed':
      return (
        <>
          <span className="queue-error">{item.state.message}</span>
          <span className="queue-actions">
            <button type="button" className="btn btn-sm" onClick={() => onUploadNow([item])}>
              Retry
            </button>
            <button type="button" className="btn btn-sm" onClick={() => onDismiss([item.id])}>
              Dismiss
            </button>
          </span>
        </>
      )

    case 'needs-decision':
      return (
        <>
          <span className="queue-warn">{item.problem?.message}</span>
          <span className="queue-actions">
            <button type="button" className="btn btn-sm" onClick={() => onUploadNow([item])}>
              Upload anyway
            </button>
            <button type="button" className="btn btn-sm" onClick={() => onDismiss([item.id])}>
              Dismiss
            </button>
          </span>
        </>
      )

    case 'blocked':
      // No upload button: upload-link would answer 400.
      return (
        <>
          <span className="queue-error">{item.problem?.message}</span>
          <span className="queue-actions">
            <button type="button" className="btn btn-sm" onClick={() => onDismiss([item.id])}>
              Dismiss
            </button>
          </span>
        </>
      )
  }
}
