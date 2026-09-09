import { useCallback, useEffect, useRef, useState } from 'react'
import { marked } from 'marked'
import { fetchNote, fetchNotes } from '../api/notes'
import type { Note, NoteSummary } from '../api/notes'
import { useResourceChanges } from '../hooks/useResourceChanges'

type ListState =
  | { status: 'loading' }
  | { status: 'ready'; notes: NoteSummary[] }
  | { status: 'error'; message: string }

type BodyState =
  | { status: 'loading' }
  | { status: 'ready'; note: Note }
  | { status: 'error'; message: string }

export function NotesList() {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [body, setBody] = useState<BodyState | null>(null)

  // Two reloads can be in flight at once - the mount and a change-stream event. Only the
  // newest may write to the list. Same guard on the body: a fast collapse-then-expand of
  // another note must not be overwritten by the first fetch landing late.
  const latestReload = useRef(0)
  const latestBody = useRef(0)

  const reload = useCallback(() => {
    const reloadId = ++latestReload.current

    void fetchNotes()
      .then((notes) => {
        if (reloadId === latestReload.current) {
          setState({ status: 'ready', notes })
        }
      })
      .catch((error: unknown) => {
        if (reloadId !== latestReload.current) {
          return
        }

        // A reload nobody asked for may fail quietly: the rows on screen are still the best
        // answer, and blanking them on a flaky connection would be worse.
        setState((current) =>
          current.status === 'ready' ? current : { status: 'error', message: messageOf(error) },
        )
      })
  }, [])

  useEffect(reload, [reload])

  useResourceChanges('notes', reload)

  async function toggle(id: string) {
    if (id === expandedId) {
      setExpandedId(null)
      setBody(null)
      return
    }

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

      {state.status === 'ready' && state.notes.length === 0 && (
        <p>No notes yet — upload a text file and the pipeline will make one.</p>
      )}

      {state.status === 'ready' && state.notes.length > 0 && (
        <ul className="notes-list">
          {state.notes.map((note) => {
            const expanded = note.id === expandedId

            return (
              <li className="note" key={note.id}>
                <button
                  type="button"
                  className="note-head"
                  aria-expanded={expanded}
                  onClick={() => void toggle(note.id)}
                >
                  <span className="note-title">{note.title}</span>
                  <span className="note-meta">
                    {note.category} · {new Date(note.updatedAtUtc).toLocaleString()}
                  </span>
                </button>

                {expanded && body?.status === 'loading' && <p className="note-loading">Loading…</p>}

                {expanded && body?.status === 'error' && (
                  <p className="notes-error" role="alert">
                    {body.message}
                  </p>
                )}

                {expanded && body?.status === 'ready' && (
                  <div
                    className="note-body"
                    // Single-user app; the body is Markdown from the local model, the same
                    // trust level as any other row already rendered. Sanitising is a later
                    // concern, for when notes take content from elsewhere.
                    dangerouslySetInnerHTML={{ __html: renderMarkdown(body.note.body) }}
                  />
                )}
              </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

function renderMarkdown(markdown: string): string {
  return marked.parse(markdown, { async: false })
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
