import { useEffect, useRef, useState } from 'react'
import { createTag, fetchTags } from '../api/tags'
import type { Tag } from '../api/tags'

const DEBOUNCE_MS = 200
const SEARCH_LIMIT = 8
const SPELLING_LIMIT = 5

/** A searchable tag picker. Closed input opens two ranked sections: the pipeline's semantic
 *  `suggestion` first, then tags with a name close to `source`'s (backend rank, see
 *  TagsController.List). Typing switches to a live text search over the same endpoint; no
 *  match there offers "Add new tag" to grow the vocabulary on the spot. Built generic so it
 *  is not tied to the merge flow - anywhere a tag needs picking from a vocabulary that can
 *  run past a hundred entries can reuse it. */
export function TagPicker({
  source,
  suggestion,
  excludeIds = [],
  onPick,
  disabled = false,
  placeholder = 'Merge into…',
  ariaLabel,
}: {
  source: Tag
  suggestion?: Tag
  /** Extra tag ids to leave out of both lists, beyond `source` itself - e.g. its current parents. */
  excludeIds?: string[]
  onPick: (tag: Tag) => void
  disabled?: boolean
  placeholder?: string
  ariaLabel: string
}) {
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const [spelling, setSpelling] = useState<Tag[]>([])
  const [results, setResults] = useState<Tag[]>([])
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement>(null)

  const term = text.trim()
  // A stable dependency: excludeIds is a fresh array on every render (callers often pass a
  // literal), which would re-run the effect - and re-debounce - on every keystroke.
  const excludeKey = excludeIds.join(',')

  useEffect(() => {
    if (!open) {
      return
    }

    const handle = setTimeout(() => {
      if (term.length === 0) {
        void fetchTags({ query: source.name, excludeId: source.id }).then((tags) =>
          setSpelling(
            tags.filter((tag) => tag.id !== suggestion?.id && !excludeIds.includes(tag.id)).slice(0, SPELLING_LIMIT),
          ),
        )
      } else {
        void fetchTags({ query: term, excludeId: source.id }).then((tags) =>
          setResults(tags.filter((tag) => !excludeIds.includes(tag.id)).slice(0, SEARCH_LIMIT)),
        )
      }
    }, DEBOUNCE_MS)

    return () => clearTimeout(handle)
    // oxlint-disable-next-line react-hooks/exhaustive-deps -- excludeKey is the stable stand-in for excludeIds, see above
  }, [term, source.id, source.name, suggestion?.id, excludeKey, open])

  useEffect(() => {
    function onClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    document.addEventListener('mousedown', onClickOutside)
    return () => document.removeEventListener('mousedown', onClickOutside)
  }, [])

  function pick(tag: Tag) {
    onPick(tag)
    setText('')
    setOpen(false)
  }

  async function addNew() {
    setCreateError(null)
    setCreating(true)

    try {
      pick(await createTag(term))
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Unexpected error')
    } finally {
      setCreating(false)
    }
  }

  return (
    <div className="tag-picker" ref={containerRef}>
      <input
        type="text"
        value={text}
        placeholder={placeholder}
        disabled={disabled}
        aria-label={ariaLabel}
        onFocus={() => setOpen(true)}
        onChange={(event) => {
          setText(event.target.value)
          setCreateError(null)
          setOpen(true)
        }}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            setOpen(false)
          }
        }}
      />

      {open && (
        <div className="tag-picker-panel">
          {term.length === 0 ? (
            <>
              {suggestion !== undefined && (
                <>
                  <p className="tag-picker-heading">Suggested</p>
                  <ul className="tag-picker-results tag-picker-suggested">
                    <li>
                      <button type="button" onClick={() => pick(suggestion)}>
                        {suggestion.name}
                      </button>
                    </li>
                  </ul>
                </>
              )}

              {spelling.length > 0 && (
                <>
                  <p className="tag-picker-heading">Similar spelling</p>
                  <ul className="tag-picker-results">
                    {spelling.map((tag) => (
                      <li key={tag.id}>
                        <button type="button" onClick={() => pick(tag)}>
                          {tag.name}
                        </button>
                      </li>
                    ))}
                  </ul>
                </>
              )}
            </>
          ) : results.length > 0 ? (
            <ul className="tag-picker-results">
              {results.map((tag) => (
                <li key={tag.id}>
                  <button type="button" onClick={() => pick(tag)}>
                    {tag.name}
                  </button>
                </li>
              ))}
            </ul>
          ) : (
            <button type="button" className="tag-picker-add" disabled={creating} onClick={() => void addNew()}>
              {creating ? 'Adding…' : `Add new tag “${term}”`}
            </button>
          )}

          {createError !== null && <p className="tag-picker-error">{createError}</p>}
        </div>
      )}
    </div>
  )
}
