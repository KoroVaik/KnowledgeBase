import { useEffect, useState } from 'react'
import { deleteAsset, fetchDownloadUrl, processAsset } from '../../api/assets'
import type { AssetSummary } from '../../api/assets'
import { addNoteTag, deleteNote, fetchNote, processNoteAgain } from '../../api/notes'
import type { Note } from '../../api/notes'
import type { Tag } from '../../api/tags'
import { isImage, isProcessable } from '../../assetKind'
import { startDownload } from '../../download'

export type Tab = 'note' | 'file'

export type NoteState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'ready'; note: Note }
  | { status: 'error'; message: string }

export type PreviewState =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'ready'; url: string }
  | { status: 'error'; message: string }

/** State and actions behind FilePanel: the note/preview fetches, and process/download/delete. */
export function useFilePanel(
  asset: AssetSummary,
  onChanged: () => void,
  onDeleted: (storedFileName: string) => void,
) {
  const image = isImage(asset)
  const hasNote = asset.noteId !== null
  const showProcess = hasNote || isProcessable(asset)
  const pending = asset.processingStatus === 'Pending' || asset.processingStatus === 'Running'

  const [tab, setTab] = useState<Tab>(hasNote ? 'note' : 'file')
  // Seeded here, not set inside the effect: a synchronous setState in an effect trips oxlint
  // react(set-state-in-effect), and the parent's key gives us a fresh mount per note anyway.
  const [note, setNote] = useState<NoteState>(hasNote ? { status: 'loading' } : { status: 'idle' })
  const [preview, setPreview] = useState<PreviewState>(
    image ? { status: 'loading' } : { status: 'unavailable' },
  )
  const [busy, setBusy] = useState<'process' | 'download' | 'delete' | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  const [addingTag, setAddingTag] = useState(false)
  const [tagError, setTagError] = useState<string | null>(null)

  useEffect(() => {
    if (asset.noteId === null) {
      return
    }

    let cancelled = false

    void fetchNote(asset.noteId)
      .then((loaded) => {
        if (!cancelled) {
          setNote({ status: 'ready', note: loaded })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setNote({ status: 'error', message: messageOf(error) })
        }
      })

    return () => {
      cancelled = true
    }
  }, [asset.noteId])

  useEffect(() => {
    if (!image) {
      return
    }

    let cancelled = false

    // The signed URL expires in minutes; that is fine for a preview open right now.
    void fetchDownloadUrl(asset.storedFileName)
      .then((url) => {
        if (!cancelled) {
          setPreview({ status: 'ready', url })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setPreview({ status: 'error', message: messageOf(error) })
        }
      })

    return () => {
      cancelled = true
    }
  }, [image, asset.storedFileName])

  async function handleProcess() {
    setActionError(null)
    setBusy('process')

    try {
      await (asset.noteId !== null
        ? processNoteAgain(asset.noteId)
        : processAsset(asset.storedFileName))
      onChanged()
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusy(null)
    }
  }

  async function handleDownload() {
    setActionError(null)
    setBusy('download')

    try {
      startDownload(await fetchDownloadUrl(asset.storedFileName))
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusy(null)
    }
  }

  async function handleAddTag(tag: Tag) {
    if (note.status !== 'ready') {
      return
    }

    setTagError(null)
    setAddingTag(true)

    try {
      const tags = await addNoteTag(note.note.id, tag.id)
      setNote({ status: 'ready', note: { ...note.note, tags } })
      onChanged()
    } catch (error) {
      setTagError(messageOf(error))
    } finally {
      setAddingTag(false)
    }
  }

  function requestDelete() {
    if (note.status === 'ready') {
      setConfirmingDelete(true)
    } else {
      void handleDeleteFile()
    }
  }

  async function handleDeleteFile() {
    if (!window.confirm(`Delete ${asset.originalFileName}? This cannot be undone.`)) {
      return
    }

    setActionError(null)
    setBusy('delete')

    try {
      await deleteAsset(asset.storedFileName)
      onDeleted(asset.storedFileName)
    } catch (error) {
      setActionError(messageOf(error))
      setBusy(null)
    }
  }

  async function handleDeleteNote(deleteSource: boolean) {
    if (asset.noteId === null) {
      return
    }

    setActionError(null)
    setBusy('delete')

    try {
      await deleteNote(asset.noteId, deleteSource)
      setConfirmingDelete(false)

      if (deleteSource) {
        onDeleted(asset.storedFileName)
      } else {
        onChanged()
      }
    } catch (error) {
      setActionError(messageOf(error))
      setBusy(null)
    }
  }

  return {
    showProcess,
    hasNote,
    pending,
    tab,
    setTab,
    note,
    preview,
    busy,
    confirmingDelete,
    setConfirmingDelete,
    actionError,
    addingTag,
    tagError,
    handleProcess,
    handleDownload,
    requestDelete,
    handleDeleteNote,
    handleAddTag,
  }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
