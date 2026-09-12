import type { AssetSummary } from '../../api/assets'
import { useFilePanel } from './useFilePanel'
import { renderNoteBody } from '../../notes/renderNoteBody'
import { DeleteNoteDialog } from '../DeleteNoteDialog/DeleteNoteDialog'
import { TagChips } from '../TagChips'
import { TagPicker } from '../TagPicker/TagPicker'
import './FilePanel.css'

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
  const {
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
  } = useFilePanel(asset, onChanged, onDeleted)

  return (
    <div className="file-panel">
      <div className="file-panel-toolbar">
        <div className="file-panel-tabs" role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'note'}
            className={tab === 'note' ? 'pill pill-on' : 'pill'}
            onClick={() => setTab('note')}
          >
            Note
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'file'}
            className={tab === 'file' ? 'pill pill-on' : 'pill'}
            onClick={() => setTab('file')}
          >
            File
          </button>
        </div>

        <div className="file-panel-actions">
          {showProcess && (
            <button
              type="button"
              className="btn btn-md"
              onClick={() => void handleProcess()}
              disabled={busy !== null || pending}
            >
              {busy === 'process' ? 'Queueing…' : hasNote ? 'Process again' : 'Process'}
            </button>
          )}

          <button type="button" className="btn btn-md" onClick={requestDelete} disabled={busy !== null}>
            {busy === 'delete' ? 'Deleting…' : 'Delete'}
          </button>

          <button type="button" className="btn btn-md" onClick={() => void handleDownload()} disabled={busy !== null}>
            {busy === 'download' ? 'Preparing…' : 'Download'}
          </button>
        </div>
      </div>

      {actionError !== null && (
        <p className="file-panel-error" role="alert">
          {actionError}
        </p>
      )}

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
            <>
              <div className="file-panel-tags">
                <div className="file-panel-tags-row">
                  {note.note.tags.length > 0 ? (
                    <TagChips tags={note.note.tags} />
                  ) : (
                    <p className="tags-empty">No tags yet.</p>
                  )}
                  <TagPicker
                    ariaLabel="Add a tag to this note"
                    placeholder="Add tag…"
                    disabled={addingTag}
                    onPick={(tag) => void handleAddTag(tag)}
                  />
                </div>
                {tagError !== null && (
                  <p className="file-panel-error" role="alert">
                    {tagError}
                  </p>
                )}
              </div>
              <div
                className="note-body"
                // No sanitising: single-user app, body is Markdown from the local model.
                dangerouslySetInnerHTML={{
                  __html: renderNoteBody(note.note.body, note.note.links, { linkable: false }),
                }}
              />
            </>
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
