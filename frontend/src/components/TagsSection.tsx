import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchNotes } from '../api/notes'
import { fetchTags, synthesiseIndex, synthesiseTag } from '../api/tags'
import type { Tag } from '../api/tags'
import { useResourceChanges } from '../hooks/useResourceChanges'

type State =
  | { status: 'loading' }
  | { status: 'ready'; tags: Tag[]; synthesisCount: number }
  | { status: 'error'; message: string }

const MIN_NOTES = 2

export function TagsSection() {
  const [state, setState] = useState<State>({ status: 'loading' })
  const [busy, setBusy] = useState<string | null>(null)
  const [queued, setQueued] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)

  const latestReload = useRef(0)

  const reload = useCallback(() => {
    const reloadId = ++latestReload.current

    void Promise.all([fetchTags(), fetchNotes('Synthesis')])
      .then(([tags, syntheses]) => {
        if (reloadId !== latestReload.current) {
          return
        }

        setState({ status: 'ready', tags, synthesisCount: syntheses.length })
        setQueued([])
      })
      .catch((err: unknown) => {
        if (reloadId !== latestReload.current) {
          return
        }

        setState((current) =>
          current.status === 'ready' ? current : { status: 'error', message: messageOf(err) },
        )
      })
  }, [])

  useEffect(reload, [reload])
  useResourceChanges('notes', reload)

  async function run(label: string, action: () => Promise<void>) {
    setError(null)
    setBusy(label)

    try {
      await action()
      setQueued((current) => [...current, label])
    } catch (err) {
      setError(messageOf(err))
    } finally {
      setBusy(null)
    }
  }

  if (state.status === 'loading') {
    return null
  }

  if (state.status === 'error') {
    return (
      <section className="tags-section">
        <h3>Tags</h3>
        <p className="notes-error" role="alert">
          {state.message}
        </p>
      </section>
    )
  }

  const withEnough = state.tags.filter((tag) => tag.noteCount >= MIN_NOTES)

  return (
    <section className="tags-section">
      <div className="tags-head">
        <h3>Tags</h3>
        <button
          type="button"
          onClick={() => void run('index', synthesiseIndex)}
          disabled={busy !== null || state.synthesisCount < MIN_NOTES}
          title={
            state.synthesisCount < MIN_NOTES
              ? 'Needs at least two synthesis notes'
              : 'Merge every synthesis note into one Contents note'
          }
        >
          {queued.includes('index') ? 'Index queued' : 'Build index'}
        </button>
      </div>

      {error !== null && (
        <p className="notes-error" role="alert">
          {error}
        </p>
      )}

      {withEnough.length === 0 && (
        <p className="tags-empty">No tag has {MIN_NOTES} notes yet — nothing to synthesise.</p>
      )}

      {withEnough.length > 0 && (
        <ul className="tags-list">
          {withEnough.map((tag) => (
            <li key={tag.name} className="tags-row">
              <span className={tag.confirmed ? 'tag-chip' : 'tag-chip tag-chip-unconfirmed'}>
                {tag.name}
              </span>
              <span className="tags-count">{tag.noteCount} notes</span>
              <button
                type="button"
                onClick={() => void run(tag.name, () => synthesiseTag(tag.name))}
                disabled={busy !== null}
              >
                {queued.includes(tag.name) ? 'Queued' : 'Synthesise'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
