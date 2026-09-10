import { useCallback, useEffect, useRef, useState } from 'react'
// Aliased because the DOM has a DragEvent of its own, and the window listener below needs it.
import type { ChangeEvent, DragEvent as ReactDragEvent } from 'react'
import { useUploadQueue } from '../upload/useUploadQueue'
import { UploadQueueDialog } from './UploadQueueDialog'

interface UploadDropZoneProps {
  onUploaded: () => void
  uploadEnabled: boolean
  /** From /api/features: the pipeline's character budget, a warning threshold. */
  maxSourceChars: number
  /** From /api/features: the hard cap upload-link refuses. */
  maxUploadBytes: number
}

export function UploadDropZone({
  onUploaded,
  uploadEnabled,
  maxSourceChars,
  maxUploadBytes,
}: UploadDropZoneProps) {
  const { items, add, uploadNow, dismiss, reset } = useUploadQueue(
    { maxUploadBytes, maxSourceChars },
    onUploaded,
  )

  const [open, setOpen] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [skippedFolders, setSkippedFolders] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  const close = useCallback(() => {
    setOpen(false)
    reset()
  }, [reset])

  /**
   * Missing the zone is easy, and a file dropped anywhere else makes the browser navigate away
   * to display it - taking the whole SPA, the session and any upload in flight with it. This
   * also covers the zone while it is disabled, where the button swallows the event and our own
   * handler never runs.
   */
  useEffect(() => {
    const swallow = (event: DragEvent) => event.preventDefault()

    window.addEventListener('dragover', swallow)
    window.addEventListener('drop', swallow)

    return () => {
      window.removeEventListener('dragover', swallow)
      window.removeEventListener('drop', swallow)
    }
  }, [])

  // Closing itself is the whole point of the queue: it is done when no row is left waiting on
  // the user. `every` on an empty list is true, so dismissing the last row closes it too.
  // The pause is only so the last row can be seen turning green.
  useEffect(() => {
    if (!open || !items.every((item) => item.state.status === 'done')) {
      return
    }

    const timer = window.setTimeout(close, 600)
    return () => window.clearTimeout(timer)
  }, [open, items, close])

  function accept(files: File[]) {
    if (!uploadEnabled || files.length === 0) {
      return
    }

    add(files)
    setOpen(true)
  }

  function handleDrop(event: ReactDragEvent<HTMLElement>) {
    // Without this the browser navigates away to display the dropped file.
    event.preventDefault()
    setDragging(false)

    const { files, folders } = readDrop(event.dataTransfer)

    setSkippedFolders(folders)
    accept(files)
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    setSkippedFolders(0)
    accept(Array.from(event.target.files ?? []))
    // <input type="file"> is uncontrolled, and picking the same file twice in a row raises no
    // change event unless the value is cleared in between.
    event.target.value = ''
  }

  return (
    <section className="upload">
      {!uploadEnabled && <p className="upload-disabled">Uploading is turned off.</p>}

      {/* A button, so a keyboard reaches the picker and a screen reader calls it what it is.
          The input is a sibling rather than a child on purpose: input.click() dispatches a
          click that bubbles, and from inside the button that would re-enter this handler. */}
      <button
        type="button"
        className={dragging ? 'drop-zone drop-zone-active' : 'drop-zone'}
        disabled={!uploadEnabled}
        onClick={() => inputRef.current?.click()}
        onDragOver={(event) => {
          // A drop target is defined by preventing the default on dragover, not by the handler
          // on drop - without this the drop event never fires at all.
          event.preventDefault()
          setDragging(true)
        }}
        onDragLeave={(event) => {
          // dragleave also fires when the cursor crosses onto a child element, which on its
          // own makes the highlight flicker. It is a real leave only when the cursor has
          // landed outside the zone entirely.
          if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
            setDragging(false)
          }
        }}
        onDrop={handleDrop}
      >
        <span className="drop-zone-title">Drop files here</span>
        <span className="drop-zone-hint">or click to choose. Several at once is fine.</span>
      </button>

      <input
        id="note-file"
        ref={inputRef}
        type="file"
        multiple
        hidden
        onChange={handleFileChange}
      />

      {skippedFolders > 0 && (
        <p className="upload-hint" role="status">
          {skippedFolders === 1 ? 'A folder was skipped' : `${skippedFolders} folders were skipped`}
          {' '}— drop the files inside it instead.
        </p>
      )}

      <UploadQueueDialog
        open={open}
        items={items}
        onUploadNow={uploadNow}
        onDismiss={dismiss}
        onClose={close}
      />
    </section>
  )
}

/**
 * A dropped folder arrives in `dataTransfer.files` as an entry with size 0, indistinguishable
 * from an empty file - so it would land in the queue as "The file is empty", which is a lie.
 * `webkitGetAsEntry` is the only way to tell them apart, and it has to be called synchronously:
 * the item list is emptied as soon as the drop handler returns.
 */
function readDrop(transfer: DataTransfer): { files: File[]; folders: number } {
  const items = Array.from(transfer.items).filter((item) => item.kind === 'file')

  if (items.length === 0) {
    return { files: Array.from(transfer.files), folders: 0 }
  }

  const files: File[] = []
  let folders = 0

  for (const item of items) {
    if (item.webkitGetAsEntry()?.isDirectory === true) {
      folders += 1
      continue
    }

    const file = item.getAsFile()

    if (file !== null) {
      files.push(file)
    }
  }

  return { files, folders }
}
