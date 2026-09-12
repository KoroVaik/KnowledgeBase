import { useCallback, useEffect, useRef, useState } from 'react'
// The DOM has its own DragEvent; the window listener below needs it.
import type { ChangeEvent, DragEvent as ReactDragEvent } from 'react'
import { useUploadQueue } from './useUploadQueue'

const GENERIC_PASTE_NAME = /^image\.[a-z0-9]+$/i

interface UseUploadDropZoneOptions {
  onUploaded: () => void
  /** From /api/features: the pipeline's character budget, a warning threshold. */
  maxSourceChars: number
  /** From /api/features: the hard cap upload-link refuses. */
  maxUploadBytes: number
}

/** State and handlers behind the drop zone: drag/drop, the file picker, paste and clipboard,
 *  and the queue dialog's open/close lifecycle - the queue itself is `useUploadQueue`. */
export function useUploadDropZone({ onUploaded, maxSourceChars, maxUploadBytes }: UseUploadDropZoneOptions) {
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

  // A file dropped past the zone makes the browser navigate to it, taking the SPA with it.
  useEffect(() => {
    const swallow = (event: DragEvent) => event.preventDefault()

    window.addEventListener('dragover', swallow)
    window.addEventListener('drop', swallow)

    return () => {
      window.removeEventListener('dragover', swallow)
      window.removeEventListener('drop', swallow)
    }
  }, [])

  // Done when no row is still waiting on the user (`every` on [] is true). The pause lets the
  // last row be seen turning green.
  useEffect(() => {
    if (!open || !items.every((item) => item.state.status === 'done')) {
      return
    }

    const timer = window.setTimeout(close, 600)
    return () => window.clearTimeout(timer)
  }, [open, items, close])

  const accept = useCallback(
    (files: File[]) => {
      if (files.length === 0) {
        return
      }

      add(files)
      setOpen(true)
    },
    [add],
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

  // Ctrl+V anywhere, and with it the Windows clipboard history (Win+V arrives as a paste).
  // On the window, not the field, so neither needs focus. preventDefault only once files were
  // taken, or pasting text elsewhere breaks.
  useEffect(() => {
    const onPaste = (event: ClipboardEvent) => {
      const accepted = acceptPasted(event.clipboardData)

      // The field is a paste target, not a text box - nothing lands in it.
      if (accepted || event.target === pasteRef.current) {
        event.preventDefault()
      }

      if (!accepted && event.target === pasteRef.current) {
        setClipboardNote('No file in what was pasted.')
      }
    }

    window.addEventListener('paste', onPaste)
    return () => window.removeEventListener('paste', onPaste)
  }, [acceptPasted])

  function handleDrop(event: ReactDragEvent<HTMLElement>) {
    // Without this the browser navigates to the dropped file.
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
    // Uncontrolled input: re-picking the same file raises no change event unless value is cleared.
    event.target.value = ''
  }

  // Not disabled by clipboard content: reading is what raises the permission prompt, so probing
  // to disable the button would raise it anyway. An empty clipboard is reported after the click.
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
      // A denied permission and a dismissed Safari prompt are indistinguishable - one message.
      setClipboardNote('The browser did not grant access to the clipboard.')
      return
    }

    if (files.length === 0) {
      setClipboardNote('No image in the clipboard.')
      return
    }

    accept(files)
  }

  return {
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
  }
}

/** Clipboard images are bare blobs; a name has to be made up - the queue and API key off one. */
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

/** A pasted whole file (copied in Explorer/Finder) keeps its real name; a screenshot blob
 *  (always `image.png`) is renamed like the button's path. */
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

/** A dropped folder is a size-0 entry, same as an empty file. `webkitGetAsEntry` tells them
 *  apart and must be called synchronously - the item list empties when the handler returns. */
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
