import { useEffect, useRef, useState } from 'react'
import type { ReactElement } from 'react'
import { fetchTags } from '../../api/tags'
import type { Tag } from '../../api/tags'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'

const CONFIRMED_PREVIEW = 10
const SEARCH_DEBOUNCE_MS = 200

/** The confirmed vocabulary can run past a hundred tags, so it shows a short preview and
 *  leaves finding a specific one to the search box, ranked by the same endpoint the merge
 *  picker uses (see TagsController.List) rather than a client-side sort. */
export function ConfirmedTags({
  tags,
  renderRow,
}: {
  tags: Tag[]
  renderRow: (tag: Tag) => ReactElement
}) {
  const [query, setQuery] = useState('')
  // Only ever written from the fetch below - the empty-query case is derived at render time,
  // not reset here, so clearing the box doesn't need a synchronous setState in the effect.
  const [results, setResults] = useState<{ term: string; ids: string[] } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const latestSearch = useRef(0)
  const { collapsed, toggle } = useCollapsibleSection('tags:confirmed')

  const term = query.trim()

  useEffect(() => {
    if (term === '') {
      return
    }

    const searchId = ++latestSearch.current
    const handle = window.setTimeout(() => {
      void fetchTags({ query: term })
        .then((matches) => {
          if (searchId !== latestSearch.current) {
            return
          }

          setResults({ term, ids: matches.filter((tag) => tag.confirmed).map((tag) => tag.id) })
          setError(null)
        })
        .catch((err: unknown) => {
          if (searchId !== latestSearch.current) {
            return
          }

          setError(err instanceof Error ? err.message : 'Unexpected error')
        })
    }, SEARCH_DEBOUNCE_MS)

    return () => window.clearTimeout(handle)
  }, [term])

  const byId = new Map(tags.map((tag) => [tag.id, tag]))
  const searching = term !== '' && results?.term !== term && error === null
  const visible =
    term === ''
      ? tags.slice(0, CONFIRMED_PREVIEW)
      : results?.term === term
        ? results.ids.map((id) => byId.get(id)).filter((tag): tag is Tag => tag !== undefined)
        : []

  return (
    <div className="subsection-panel">
      <h3 className="tags-subhead">
        <button type="button" className="subsection-toggle" aria-expanded={!collapsed} onClick={toggle}>
          <span className="section-toggle-caret" aria-hidden="true">▾</span>
          Confirmed
          <span className="section-count">{tags.length}</span>
        </button>
      </h3>

      {!collapsed && (
        <>
          <input
            type="text"
            className="tags-search field field-xs"
            placeholder="Search tags…"
            aria-label="Search confirmed tags"
            value={query}
            onChange={(event) => setQuery(event.target.value)}
          />

          {error !== null && (
            <p className="notes-error" role="alert">
              {error}
            </p>
          )}

          {searching && <p className="tags-empty">Searching…</p>}

          {!searching && term !== '' && visible.length === 0 && error === null && (
            <p className="tags-empty">No matches.</p>
          )}

          {!searching && <ul className="tags-list">{visible.map(renderRow)}</ul>}

          {term === '' && tags.length > CONFIRMED_PREVIEW && (
            <p className="tags-more">
              Showing {CONFIRMED_PREVIEW} of {tags.length}.
            </p>
          )}
        </>
      )}
    </div>
  )
}
