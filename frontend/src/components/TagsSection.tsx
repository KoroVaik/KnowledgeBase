import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchNotes } from '../api/notes'
import {
  addTagParent,
  confirmTag,
  deleteTag,
  fetchTags,
  mergeTag,
  removeTagParent,
  suggestTagMerges,
  synthesiseIndex,
  synthesiseTag,
} from '../api/tags'
import type { Tag } from '../api/tags'
import { useResourceChanges } from '../hooks/useResourceChanges'
import { TagPicker } from './TagPicker'

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

  async function run(key: string, action: () => Promise<void>): Promise<boolean> {
    setError(null)
    setBusy(key)

    try {
      await action()
      return true
    } catch (err) {
      setError(messageOf(err))
      return false
    } finally {
      setBusy(null)
    }
  }

  async function synthesise(label: string, action: () => Promise<void>) {
    if (await run(label, action)) {
      setQueued((current) => [...current, label])
    }
  }

  async function change(key: string, action: () => Promise<void>) {
    if (await run(key, action)) {
      reload()
    }
  }

  function remove(tag: Tag) {
    if (
      tag.confirmed &&
      !window.confirm(
        `Delete “${tag.name}”? It comes off ${notesText(tag.noteCount)}, and its synthesis note goes to the bin.`,
      )
    ) {
      return
    }

    void change(tag.id, () => deleteTag(tag.id))
  }

  function merge(tag: Tag, into: Tag, ask: boolean) {
    if (
      ask &&
      !window.confirm(
        `Merge “${tag.name}” into “${into.name}”? Its ${notesText(tag.noteCount)} move to “${into.name}”, and the synthesis note of “${tag.name}” goes to the bin.`,
      )
    ) {
      return
    }

    void change(tag.id, () => mergeTag(tag.id, into.id))
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

  const { tags } = state
  const toReview = tags.filter((tag) => !tag.confirmed)
  const confirmed = tags.filter((tag) => tag.confirmed)

  function row(tag: Tag) {
    const suggestion = tags.find((other) => other.id === tag.suggestedMergeIntoId)
    const parents = tag.parentIds
      .map((id) => tags.find((other) => other.id === id))
      .filter((parent): parent is Tag => parent !== undefined)

    return (
      <li key={tag.id} className="tags-row">
        <span className={tag.confirmed ? 'tag-chip' : 'tag-chip tag-chip-unconfirmed'}>{tag.name}</span>
        <span className="tags-count">{notesText(tag.noteCount)}</span>

        <span className="tags-actions">
          {!tag.confirmed && (
            <button type="button" onClick={() => void change(tag.id, () => confirmTag(tag.id))} disabled={busy !== null}>
              Confirm
            </button>
          )}

          {tag.noteCount >= MIN_NOTES && (
            <button
              type="button"
              onClick={() => void synthesise(tag.name, () => synthesiseTag(tag.name))}
              disabled={busy !== null}
            >
              {queued.includes(tag.name) ? 'Queued' : 'Synthesise'}
            </button>
          )}

          <TagPicker
            source={tag}
            suggestion={suggestion}
            ariaLabel={`Merge ${tag.name} into another tag`}
            disabled={busy !== null}
            onPick={(into) => merge(tag, into, true)}
          />

          <button type="button" onClick={() => remove(tag)} disabled={busy !== null}>
            Delete
          </button>
        </span>

        <span className="tags-parents">
          <span className="tags-parents-label">Parent</span>

          {parents.map((parent) => (
            <span key={parent.id} className="tag-chip tags-parent-chip">
              {parent.name}
              <button
                type="button"
                aria-label={`Remove ${parent.name} as a parent of ${tag.name}`}
                onClick={() => void change(tag.id, () => removeTagParent(tag.id, parent.id))}
                disabled={busy !== null}
              >
                ×
              </button>
            </span>
          ))}

          <TagPicker
            source={tag}
            excludeIds={tag.parentIds}
            placeholder="Add parent…"
            ariaLabel={`Add a parent to ${tag.name}`}
            disabled={busy !== null}
            onPick={(parent) => void change(tag.id, () => addTagParent(tag.id, parent.id))}
          />
        </span>
      </li>
    )
  }

  return (
    <section className="tags-section">
      <div className="tags-head">
        <h3>Tags</h3>
        <span className="tags-head-actions">
          <button
            type="button"
            onClick={() => void synthesise('suggest-merges', suggestTagMerges)}
            disabled={busy !== null || toReview.length === 0 || confirmed.length === 0}
            title="Re-check every unreviewed tag against the confirmed vocabulary"
          >
            {queued.includes('suggest-merges') ? 'Suggestions queued' : 'Suggest merges'}
          </button>

          <button
            type="button"
            onClick={() => void synthesise('index', synthesiseIndex)}
            disabled={busy !== null || state.synthesisCount < MIN_NOTES}
            title={
              state.synthesisCount < MIN_NOTES
                ? 'Needs at least two synthesis notes'
                : 'Merge every synthesis note into one Contents note'
            }
          >
            {queued.includes('index') ? 'Index queued' : 'Build index'}
          </button>
        </span>
      </div>

      {error !== null && (
        <p className="notes-error" role="alert">
          {error}
        </p>
      )}

      {tags.length === 0 && <p className="tags-empty">No tags yet.</p>}

      {toReview.length > 0 && (
        <>
          <h4 className="tags-subhead">To review</h4>
          <ul className="tags-list">{toReview.map(row)}</ul>
        </>
      )}

      {confirmed.length > 0 && (
        <>
          {toReview.length > 0 && <h4 className="tags-subhead">Confirmed</h4>}
          <ul className="tags-list">{confirmed.map(row)}</ul>
        </>
      )}
    </section>
  )
}

function notesText(count: number): string {
  return count === 1 ? '1 note' : `${count} notes`
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
