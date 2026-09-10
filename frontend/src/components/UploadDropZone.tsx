import { useCallback, useEffect, useRef, useState } from 'react'
// Aliased because the DOM has a DragEvent of its own, and the window listener below needs it.
import type { ChangeEvent, DragEvent as ReactDragEvent } from 'react'
import { useUploadQueue } from '../upload/useUploadQueue'
import { UploadQueueDialog } from './UploadQueueDialog'

const GENERIC_PASTE_NAME = /^image\.[a-z0-9]+$/i

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
  const [clipboardNote, setClipboardNote] = useState<string | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const pasteRef = useRef<HTMLInputElement>(null)

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

  const accept = useCallback(
    (files: File[]) => {
      if (!uploadEnabled || files.length === 0) {
        return
      }

      add(files)
      setOpen(true)
    },
    [uploadEnabled, add],
  )

  const acceptPasted = useCallback(
    (transfer: DataTransfer | null): boolean => {
      const files = transfer === null ? [] : readPastedFiles(transfer)

      if (files.length === 0) {
        return false
      }

      setSkippedFolders(0)
      setClipboardNote(null)
      accept(files)

      return true
    },
    [accept],
  )

  /**
   * Ctrl+V anywhere on the page, and with it the Windows clipboard history (Win+V pastes the
   * chosen entry into the focused window, which reaches us as this same event). The listener
   * sits on the window rather than on the field below so that neither needs focus.
   *
   * The default is only prevented once files were taken: pasting text into some other input
   * has to keep working.
   */
  useEffect(() => {
    if (!uploadEnabled) {
      return
    }

    const onPaste = (event: ClipboardEvent) => {
      const accepted = acceptPasted(event.clipboardData)

      // The field is a target for the paste gesture, not a text box: whatever was pasted,
      // it never lands in it.
      if (accepted || event.target === pasteRef.current) {
        event.preventDefault()
      }

      if (!accepted && event.target === pasteRef.current) {
        setClipboardNote('No file in what was pasted.')
      }
    }

    window.addEventListener('paste', onPaste)
    return () => window.removeEventListener('paste', onPaste)
  }, [uploadEnabled, acceptPasted])

  function handleDrop(event: ReactDragEvent<HTMLElement>) {
    // Without this the browser navigates away to display the dropped file.
    event.preventDefault()
    setDragging(false)
    setClipboardNote(null)

    const { files, folders } = readDrop(event.dataTransfer)

    setSkippedFolders(folders)
    accept(files)
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    setSkippedFolders(0)
    setClipboardNote(null)
    accept(Array.from(event.target.files ?? []))
    // <input type="file"> is uncontrolled, and picking the same file twice in a row raises no
    // change event unless the value is cleared in between.
    event.target.value = ''
  }

  /**
   * The button stays enabled whatever the clipboard holds: what is in there can only be learned
   * by reading it, and reading is what asks the user for permission - Safari puts up its own
   * "Paste" prompt, Chrome a permission one. Probing to decide whether to grey out the button
   * would raise that prompt on its own, so an empty clipboard is reported after the click.
   */
  async function pasteFromClipboard() {
    setSkippedFolders(0)
    setClipboardNote(null)

    if (typeof navigator.clipboard?.read !== 'function') {
      setClipboardNote('This browser cannot read the clipboard. Drop the file or pick it instead.')
      return
    }

    let files: File[]

    try {
      files = await readClipboardImages()
    } catch {
      // Both a denied permission and a dismissed Safari prompt land here, and the browser tells
      // them apart nowhere - hence one message covering the whole "did not get the bytes" case.
      setClipboardNote('The browser did not grant access to the clipboard.')
      return
    }

    if (files.length === 0) {
      setClipboardNote('No image in the clipboard.')
      return
    }

    accept(files)
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

      {/* A real focusable field, because that is the only thing the clipboard history of a
          phone can paste into: Gboard's clipboard tab and the long-press "Paste" menu both
          insert into the focused input. On a desktop the window listener above covers Ctrl+V
          and Win+V without it. */}
      <div className="upload-actions">
        <button
          type="button"
          className="upload-action"
          disabled={!uploadEnabled}
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
          disabled={!uploadEnabled}
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

/**
 * Clipboard images arrive as bare blobs, so a name has to be made up here - the queue, the
 * classifier and the API all key off one, and a screenshot has none.
 */
async function readClipboardImages(): Promise<File[]> {
  const stamp = clipboardStamp()
  const files: File[] = []

  for (const item of await navigator.clipboard.read()) {
    const type = item.types.find((candidate) => candidate.startsWith('image/'))

    if (type === undefined) {
      continue
    }

    const blob = await item.getType(type)
    const suffix = files.length === 0 ? '' : `-${files.length + 1}`

    files.push(new File([blob], `clipboard-${stamp}${suffix}.${extensionOf(type)}`, { type }))
  }

  return files
}

/**
 * A paste carries whole files too - a file copied in Explorer or Finder arrives here with its
 * real name, and that one is kept. A screenshot arrives as a blob the browser calls `image.png`
 * no matter when it was taken, so those get the same synthetic name as the button's path.
 */
function readPastedFiles(transfer: DataTransfer): File[] {
  const stamp = clipboardStamp()

  return Array.from(transfer.files).map((file, index) => (
    GENERIC_PASTE_NAME.test(file.name) || file.name === ''
      ? renamed(file, stamp, index)
      : file
  ))
}

function renamed(file: File, stamp: string, index: number): File {
  const suffix = index === 0 ? '' : `-${index + 1}`

  return new File([file], `clipboard-${stamp}${suffix}.${extensionOf(file.type)}`, {
    type: file.type,
  })
}

function clipboardStamp(): string {
  return new Date().toISOString().replaceAll(/[:T]/g, '-').slice(0, 19)
}

function extensionOf(mime: string): string {
  const subtype = mime.slice(mime.indexOf('/') + 1).split('+')[0].toLowerCase()

  return subtype === 'jpeg' ? 'jpg' : subtype === '' ? 'bin' : subtype
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
