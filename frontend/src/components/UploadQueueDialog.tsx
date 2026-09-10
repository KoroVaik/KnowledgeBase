import { useEffect, useRef } from 'react'
import { formatSize } from '../format'
import { isBusy, needsAttention } from '../upload/useUploadQueue'
import type { QueuedItem } from '../upload/useUploadQueue'

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
  const dialogRef = useRef<HTMLDialogElement>(null)

  // Modality only exists as an imperative call - React has to reach into the DOM here.
  useEffect(() => {
    const dialog = dialogRef.current

    if (dialog === null) {
      return
    }

    if (open && !dialog.open) {
      dialog.showModal()
    } else if (!open && dialog.open) {
      dialog.close()
    }
  }, [open])

  const busy = isBusy(items)
  const warnings = items.filter((item) => item.state.status === 'needs-decision')
  const attention = items.filter(needsAttention)
  const done = items.filter((item) => item.state.status === 'done').length

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
            className="queue-primary"
            onClick={() => onUploadNow(warnings)}
          >
            Upload all anyway
          </button>
        )}

        {attention.length > 0 && (
          <button type="button" onClick={() => onDismiss(attention.map((item) => item.id))}>
            Dismiss all
          </button>
        )}

        {/* close(), so every exit path arrives through the same `close` event. */}
        <button type="button" onClick={() => dialogRef.current?.close()} disabled={busy}>
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
            <button type="button" onClick={() => onUploadNow([item])}>
              Retry
            </button>
            <button type="button" onClick={() => onDismiss([item.id])}>
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
            <button type="button" onClick={() => onUploadNow([item])}>
              Upload anyway
            </button>
            <button type="button" onClick={() => onDismiss([item.id])}>
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
            <button type="button" onClick={() => onDismiss([item.id])}>
              Dismiss
            </button>
          </span>
        </>
      )
  }
}
