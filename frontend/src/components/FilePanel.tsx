import { useEffect, useState } from 'react'
import { deleteAsset, fetchDownloadUrl, processAsset } from '../api/assets'
import type { AssetSummary } from '../api/assets'
import { deleteNote, fetchNote, processNoteAgain } from '../api/notes'
import type { Note } from '../api/notes'
import { isImage, isProcessable } from '../assetKind'
import { startDownload } from '../download'
import { renderNoteBody } from '../notes/renderNoteBody'
import { DeleteNoteDialog } from './DeleteNoteDialog'

type Tab = 'note' | 'file'

type NoteState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'ready'; note: Note }
  | { status: 'error'; message: string }

type PreviewState =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'ready'; url: string }
  | { status: 'error'; message: string }

interface FilePanelProps {
  /** Remount this on a note-id change (a re-run swaps the note) - see the key in AssetList. */
  asset: AssetSummary
  /** Reload the list after an action that changed job or note state. */
  onChanged: () => void
  /** Drop this row from the list at once, after its file is gone. */
  onDeleted: (storedFileName: string) => void
}

/** The section that opens under a file row: actions on top, then a Note / File preview. */
export function FilePanel({ asset, onChanged, onDeleted }: FilePanelProps) {
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

  return (
    <div className="file-panel">
      <div className="file-panel-actions">
        {showProcess && (
          <button type="button" onClick={() => void handleProcess()} disabled={busy !== null || pending}>
            {busy === 'process' ? 'Queueing…' : hasNote ? 'Process again' : 'Process'}
          </button>
        )}

        <button type="button" onClick={requestDelete} disabled={busy !== null}>
          {busy === 'delete' ? 'Deleting…' : 'Delete'}
        </button>

        <button type="button" onClick={() => void handleDownload()} disabled={busy !== null}>
          {busy === 'download' ? 'Preparing…' : 'Download'}
        </button>
      </div>

      {actionError !== null && (
        <p className="file-panel-error" role="alert">
          {actionError}
        </p>
      )}

      <div className="file-panel-tabs" role="tablist">
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'note'}
          className={tab === 'note' ? 'file-panel-tab file-panel-tab-on' : 'file-panel-tab'}
          onClick={() => setTab('note')}
        >
          Note
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === 'file'}
          className={tab === 'file' ? 'file-panel-tab file-panel-tab-on' : 'file-panel-tab'}
          onClick={() => setTab('file')}
        >
          File
        </button>
      </div>

      {tab === 'note' && (
        <div className="file-panel-body">
          {asset.noteId === null && <p>No note yet — the pipeline has not made one from this file.</p>}
          {note.status === 'loading' && <p>Loading…</p>}
          {note.status === 'error' && (
            <p className="file-panel-error" role="alert">
              {note.message}
            </p>
          )}
          {note.status === 'ready' && (
            <div
              className="note-body"
              // No sanitising: single-user app, body is Markdown from the local model.
              dangerouslySetInnerHTML={{
                __html: renderNoteBody(note.note.body, note.note.links, { linkable: false }),
              }}
            />
          )}
        </div>
      )}

      {tab === 'file' && (
        <div className="file-panel-body">
          {preview.status === 'unavailable' && (
            <p>Preview isn’t available for this file type yet — use Download to open it.</p>
          )}
          {preview.status === 'loading' && <p>Loading…</p>}
          {preview.status === 'error' && (
            <p className="file-panel-error" role="alert">
              {preview.message}
            </p>
          )}
          {preview.status === 'ready' && (
            <img className="file-panel-image" src={preview.url} alt={asset.originalFileName} />
          )}
        </div>
      )}

      <DeleteNoteDialog
        note={confirmingDelete && note.status === 'ready' ? note.note : null}
        busy={busy === 'delete'}
        onCancel={() => setConfirmingDelete(false)}
        onConfirm={(deleteSource) => void handleDeleteNote(deleteSource)}
      />
    </div>
  )
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
