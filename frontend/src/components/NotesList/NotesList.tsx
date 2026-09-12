import { formatDateTime } from '../../format'
import { renderNoteBody } from '../../notes/renderNoteBody'
import { useNotesList } from './useNotesList'
import { DeleteNoteDialog } from '../DeleteNoteDialog/DeleteNoteDialog'
import { NoteRow } from './NoteRow'
import { TagChips } from '../TagChips'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import './NotesList.css'

export function NotesList() {
  const {
    state,
    expandedId,
    body,
    showTrash,
    setShowTrash,
    deleting,
    setDeleting,
    deleteBusy,
    busyId,
    actionError,
    reprocessing,
    toggle,
    followLink,
    handleProcessAgain,
    handleDelete,
    handleRestore,
    handlePurge,
  } = useNotesList()
  const { collapsed, toggle: toggleSection } = useCollapsibleSection('notes')

  return (
    <section className="notes">
      <h2>
        <button type="button" className="section-toggle" aria-expanded={!collapsed} onClick={toggleSection}>
          <span className="section-toggle-caret" aria-hidden="true">▾</span>
          Notes
          {state.status === 'ready' && <span className="section-count">{state.notes.length}</span>}
        </button>
      </h2>

      {!collapsed && (
        <>
          {state.status === 'loading' && <p>Loading…</p>}

          {state.status === 'error' && (
            <p className="notes-error" role="alert">
              {state.message}
            </p>
          )}

          {actionError !== null && (
            <p className="notes-error" role="alert">
              {actionError}
            </p>
          )}

          {state.status === 'ready' && state.notes.length === 0 && (
            <p>No aggregated notes yet.</p>
          )}

          {state.status === 'ready' && state.notes.length > 0 && (
            <ul className="notes-list">
              {state.notes.map((note) => {
                const expanded = note.id === expandedId
                const busy = busyId === note.id
                const waiting = reprocessing.includes(note.id)

                return (
                  <NoteRow
                    key={note.id}
                    note={note}
                    expanded={expanded}
                    onToggle={() => void toggle(note.id)}
                    meta={
                      <>
                        <TagChips tags={note.tags} />
                        {formatDateTime(note.updatedAtUtc)}
                      </>
                    }
                    belowHead={
                      <>
                        {note.sourceAssetId === null && (
                          <span className="note-removed-source">
                            {note.sourceFileName !== null
                              ? `The file “${note.sourceFileName}” it came from is gone`
                              : 'The file it came from is gone'}
                          </span>
                        )}
                        {waiting && <span className="note-waiting">Reprocessing — a new version is on the way…</span>}
                      </>
                    }
                    actions={
                      <>
                        {note.sourceAssetId !== null && (
                          <button
                            type="button"
                            className="btn btn-md"
                            onClick={() => void handleProcessAgain(note)}
                            disabled={busy || waiting}
                            title="Run the pipeline over this file again and replace this note"
                          >
                            Process again
                          </button>
                        )}
                        <button type="button" className="btn btn-md" onClick={() => setDeleting(note)} disabled={busy}>
                          Delete
                        </button>
                      </>
                    }
                    body={
                      <>
                        {body?.status === 'loading' && <p className="note-loading">Loading…</p>}

                        {body?.status === 'error' && (
                          <p className="notes-error" role="alert">
                            {body.message}
                          </p>
                        )}

                        {body?.status === 'ready' && (
                          <div
                            className="note-body"
                            onClick={followLink}
                            // No sanitising: single-user app, body is Markdown from the local model.
                            dangerouslySetInnerHTML={{
                              __html: renderNoteBody(body.note.body, body.note.links),
                            }}
                          />
                        )}
                      </>
                    }
                  />
                )
              })}
            </ul>
          )}

          {state.status === 'ready' && state.trash.length > 0 && (
            <div className="notes-trash">
              <button
                type="button"
                className="pill"
                aria-expanded={showTrash}
                onClick={() => setShowTrash((shown) => !shown)}
              >
                Bin ({state.trash.length})
              </button>

              {showTrash && (
                <ul className="notes-list">
                  {state.trash.map((note) => {
                    const busy = busyId === note.id
                    const expanded = note.id === expandedId

                    return (
                      <NoteRow
                        key={note.id}
                        note={note}
                        binned
                        expanded={expanded}
                        onToggle={() => void toggle(note.id)}
                        meta={
                          <>
                            Deleted {note.deletedAtUtc !== null && formatDateTime(note.deletedAtUtc)}
                            {note.sourceFileName !== null && ` · from “${note.sourceFileName}”`}
                          </>
                        }
                        actions={
                          <>
                            <button type="button" className="btn btn-md" onClick={() => void handleRestore(note)} disabled={busy}>
                              Restore
                            </button>
                            <button type="button" className="btn btn-md" onClick={() => void handlePurge(note)} disabled={busy}>
                              Delete forever
                            </button>
                          </>
                        }
                        body={
                          body?.status === 'ready' && (
                            <div
                              className="note-body"
                              onClick={followLink}
                              dangerouslySetInnerHTML={{
                                __html: renderNoteBody(body.note.body, body.note.links),
                              }}
                            />
                          )
                        }
                      />
                    )
                  })}
                </ul>
              )}
            </div>
          )}

          {/* Keyed by the note: a fresh mount resets the checkbox and backlinks between openings. */}
          <DeleteNoteDialog
            key={deleting?.id ?? 'closed'}
            note={deleting}
            busy={deleteBusy}
            onCancel={() => setDeleting(null)}
            onConfirm={(deleteSource) => void handleDelete(deleteSource)}
          />
        </>
      )}
    </section>
  )
}
