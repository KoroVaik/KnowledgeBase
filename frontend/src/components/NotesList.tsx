import { useCallback, useEffect, useRef, useState } from 'react'
import {
  deleteNote,
  fetchNote,
  fetchNotes,
  fetchTrash,
  processNoteAgain,
  purgeNote,
  restoreNote,
} from '../api/notes'
import type { Note, NoteSummary } from '../api/notes'
import { formatDateTime } from '../format'
import { renderNoteBody } from '../notes/renderNoteBody'
import { DeleteNoteDialog } from './DeleteNoteDialog'
import { TagChips } from './TagChips'
import { useResourceChanges } from '../hooks/useResourceChanges'

type ListState =
  | { status: 'loading' }
  | { status: 'ready'; notes: NoteSummary[]; trash: NoteSummary[] }
  | { status: 'error'; message: string }

type BodyState =
  | { status: 'loading' }
  | { status: 'ready'; note: Note }
  | { status: 'error'; message: string }

export function NotesList() {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [body, setBody] = useState<BodyState | null>(null)
  const [showTrash, setShowTrash] = useState(false)
  const [deleting, setDeleting] = useState<NoteSummary | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  // Notes waiting for a fresh version. The worker has no change stream to the browser, so
  // poll until the watched id disappears - which is what being replaced looks like.
  const [reprocessing, setReprocessing] = useState<string[]>([])

  // Two reloads (mount + stream event) can race; only the newest writes. Same guard on the body.
  const latestReload = useRef(0)
  const latestBody = useRef(0)

  const reload = useCallback(() => {
    const reloadId = ++latestReload.current

    // Source notes now live under their file in the Files section; this list is the rest.
    void Promise.all([fetchNotes('Synthesis'), fetchTrash()])
      .then(([notes, trash]) => {
        if (reloadId !== latestReload.current) {
          return
        }

        setState({ status: 'ready', notes, trash })
        setReprocessing((current) => current.filter((id) => notes.some((note) => note.id === id)))
      })
      .catch((error: unknown) => {
        if (reloadId !== latestReload.current) {
          return
        }

        // Fail quietly: stale rows beat blanking them on a flaky connection.
        setState((current) =>
          current.status === 'ready' ? current : { status: 'error', message: messageOf(error) },
        )
      })
  }, [])

  useEffect(reload, [reload])

  useResourceChanges('notes', reload)

  useEffect(() => {
    if (reprocessing.length === 0) {
      return
    }

    const timer = window.setInterval(reload, 4000)
    return () => window.clearInterval(timer)
  }, [reprocessing, reload])

  const open = useCallback(async (id: string) => {
    const bodyId = ++latestBody.current
    setExpandedId(id)
    setBody({ status: 'loading' })

    try {
      const note = await fetchNote(id)
      if (bodyId === latestBody.current) {
        setBody({ status: 'ready', note })
      }
    } catch (error) {
      if (bodyId === latestBody.current) {
        setBody({ status: 'error', message: messageOf(error) })
      }
    }
  }, [])

  async function toggle(id: string) {
    if (id === expandedId) {
      setExpandedId(null)
      setBody(null)
      return
    }

    await open(id)
  }

  /** One handler on the container rather than one per link: the body is rendered HTML. */
  function followLink(event: React.MouseEvent<HTMLDivElement>) {
    const target = (event.target as HTMLElement).closest<HTMLElement>('[data-note-id]')
    const id = target?.dataset.noteId

    if (id === undefined) {
      return
    }

    // The target may be off screen, where opening it would look like nothing happened.
    document.querySelector(`[data-note-row="${id}"]`)?.scrollIntoView({ block: 'nearest' })
    void open(id)
  }

  async function handleProcessAgain(note: NoteSummary) {
    setActionError(null)
    setBusyId(note.id)

    try {
      await processNoteAgain(note.id)
      setReprocessing((current) => [...current, note.id])
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusyId(null)
    }
  }

  async function handleDelete(deleteSource: boolean) {
    if (deleting === null) {
      return
    }

    setActionError(null)
    setDeleteBusy(true)

    try {
      await deleteNote(deleting.id, deleteSource)

      if (expandedId === deleting.id) {
        setExpandedId(null)
        setBody(null)
      }

      setDeleting(null)
      reload()
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setDeleteBusy(false)
    }
  }

  async function handleRestore(note: NoteSummary) {
    setActionError(null)
    setBusyId(note.id)

    try {
      await restoreNote(note.id)
      reload()
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusyId(null)
    }
  }

  async function handlePurge(note: NoteSummary) {
    if (
      !window.confirm(
        `Delete “${note.title}” permanently? Links to it will stop saying it was deleted and read as “no such note” instead.`,
      )
    ) {
      return
    }

    setActionError(null)
    setBusyId(note.id)

    try {
      await purgeNote(note.id)

      if (expandedId === note.id) {
        setExpandedId(null)
        setBody(null)
      }

      reload()
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusyId(null)
    }
  }

  // Nothing to show yet: no aggregated notes and an empty bin. The section reappears once a
  // synthesis note exists or something is binned.
  if (state.status === 'ready' && state.notes.length === 0 && state.trash.length === 0) {
    return null
  }

  return (
    <section className="notes">
      <h2>Notes</h2>

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
              <li className="note" key={note.id} data-note-row={note.id}>
                <div className="note-row">
                  <button
                    type="button"
                    className="note-head"
                    aria-expanded={expanded}
                    onClick={() => void toggle(note.id)}
                  >
                    <span className="note-title">{note.title}</span>
                    <span className="note-meta">
                      <TagChips tags={note.tags} />
                      {formatDateTime(note.updatedAtUtc)}
                    </span>
                    {note.sourceAssetId === null && (
                      <span className="note-removed-source">
                        {note.sourceFileName !== null
                          ? `The file “${note.sourceFileName}” it came from is gone`
                          : 'The file it came from is gone'}
                      </span>
                    )}
                    {waiting && <span className="note-waiting">Reprocessing — a new version is on the way…</span>}
                  </button>

                  <div className="note-actions">
                    {note.sourceAssetId !== null && (
                      <button
                        type="button"
                        onClick={() => void handleProcessAgain(note)}
                        disabled={busy || waiting}
                        title="Run the pipeline over this file again and replace this note"
                      >
                        Process again
                      </button>
                    )}
                    <button type="button" onClick={() => setDeleting(note)} disabled={busy}>
                      Delete
                    </button>
                  </div>
                </div>

                {expanded && body?.status === 'loading' && <p className="note-loading">Loading…</p>}

                {expanded && body?.status === 'error' && (
                  <p className="notes-error" role="alert">
                    {body.message}
                  </p>
                )}

                {expanded && body?.status === 'ready' && (
                  <div
                    className="note-body"
                    onClick={followLink}
                    // No sanitising: single-user app, body is Markdown from the local model.
                    dangerouslySetInnerHTML={{
                      __html: renderNoteBody(body.note.body, body.note.links),
                    }}
                  />
                )}
              </li>
            )
          })}
        </ul>
      )}

      {state.status === 'ready' && state.trash.length > 0 && (
        <div className="notes-trash">
          <button
            type="button"
            className="notes-trash-toggle"
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
                  <li className="note note-binned" key={note.id} data-note-row={note.id}>
                    <div className="note-row">
                      <button
                        type="button"
                        className="note-head"
                        aria-expanded={expanded}
                        onClick={() => void toggle(note.id)}
                      >
                        <span className="note-title">{note.title}</span>
                        <span className="note-meta">
                          Deleted {note.deletedAtUtc !== null && formatDateTime(note.deletedAtUtc)}
                          {note.sourceFileName !== null && ` · from “${note.sourceFileName}”`}
                        </span>
                      </button>

                      <div className="note-actions">
                        <button type="button" onClick={() => void handleRestore(note)} disabled={busy}>
                          Restore
                        </button>
                        <button type="button" onClick={() => void handlePurge(note)} disabled={busy}>
                          Delete forever
                        </button>
                      </div>
                    </div>

                    {expanded && body?.status === 'ready' && (
                      <div
                        className="note-body"
                        onClick={followLink}
                        dangerouslySetInnerHTML={{
                          __html: renderNoteBody(body.note.body, body.note.links),
                        }}
                      />
                    )}
                  </li>
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
    </section>
  )
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
