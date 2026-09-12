import { useTagPicker } from './useTagPicker'
import type { Tag } from '../../api/tags'
import './TagPicker.css'

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
  source?: Tag
  suggestion?: Tag
  /** Extra tag ids to leave out of both lists, beyond `source` itself - e.g. its current parents. */
  excludeIds?: string[]
  onPick: (tag: Tag) => void
  disabled?: boolean
  placeholder?: string
  ariaLabel: string
}) {
  const {
    text,
    open,
    spelling,
    results,
    creating,
    createError,
    containerRef,
    term,
    pick,
    addNew,
    focus,
    updateText,
    closeOnEscape,
  } = useTagPicker({ source, suggestion, excludeIds, onPick })

  return (
    <div className="tag-picker" ref={containerRef}>
      <input
        type="text"
        className="field field-xs"
        value={text}
        placeholder={placeholder}
        disabled={disabled}
        aria-label={ariaLabel}
        onFocus={focus}
        onChange={(event) => updateText(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            closeOnEscape()
          }
        }}
      />

      {open && (
        <div className="tag-picker-panel">
          {term.length === 0 ? (
            suggestion === undefined && spelling.length === 0 ? (
              <p className="tag-picker-empty">No suggestions — start typing to search.</p>
            ) : (
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
                    <p className="tag-picker-heading">{source ? 'Similar spelling' : 'Tags'}</p>
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
            )
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
