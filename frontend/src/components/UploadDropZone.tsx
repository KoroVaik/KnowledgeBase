import { useUploadDropZone } from '../upload/useUploadDropZone'
import { UploadQueueDialog } from './UploadQueueDialog/UploadQueueDialog'
import './UploadDropZone.css'

interface UploadDropZoneProps {
  onUploaded: () => void
  /** From /api/features: the pipeline's character budget, a warning threshold. */
  maxSourceChars: number
  /** From /api/features: the hard cap upload-link refuses. */
  maxUploadBytes: number
}

export function UploadDropZone({
  onUploaded,
  maxSourceChars,
  maxUploadBytes,
}: UploadDropZoneProps) {
  const {
    items,
    uploadNow,
    dismiss,
    open,
    dragging,
    setDragging,
    skippedFolders,
    clipboardNote,
    inputRef,
    pasteRef,
    close,
    handleDrop,
    handleFileChange,
    pasteFromClipboard,
  } = useUploadDropZone({ onUploaded, maxSourceChars, maxUploadBytes })

  return (
    <section className="upload">
      {/* Input is a sibling, not a child: input.click() bubbles, and from inside the button
          that re-enters this handler. */}
      <button
        type="button"
        className={dragging ? 'drop-zone drop-zone-active' : 'drop-zone'}
        onClick={() => inputRef.current?.click()}
        onDragOver={(event) => {
          // preventDefault on dragover is what makes this a drop target; without it drop never fires.
          event.preventDefault()
          setDragging(true)
        }}
        onDragLeave={(event) => {
          // dragleave also fires crossing onto a child - real leave only if the cursor left the zone.
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

      {/* A focusable field: the only thing a phone's clipboard history can paste into. On
          desktop the window listener above covers Ctrl+V and Win+V without it. */}
      <div className="upload-actions">
        <button
          type="button"
          className="btn btn-md"
          onClick={pasteFromClipboard}
        >
          Upload from clipboard
        </button>

        <input
          ref={pasteRef}
          type="text"
          className="upload-paste"
          placeholder="…or paste here"
          aria-label="Paste a screenshot or a file here"
          autoComplete="off"
          spellCheck={false}
          onChange={(event) => {
            event.target.value = ''
          }}
        />
      </div>

      {clipboardNote !== null && (
        <p className="upload-hint" role="status">{clipboardNote}</p>
      )}

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
