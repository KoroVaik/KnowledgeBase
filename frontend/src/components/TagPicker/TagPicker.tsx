import { useTagPicker } from './useTagPicker'
import type { Tag } from '../../api/tags'
import { Dropdown } from '../Dropdown/Dropdown'
import './TagPicker.css'

/** A searchable tag picker. Closed input opens two ranked sections: the pipeline's semantic
 *  `suggestion` first, then tags with a name close to `source`'s (backend rank, see
 *  TagsController.List) - or, when nothing shares spelling with `source`'s name, the plain
 *  busiest-tags listing instead, so the panel always offers real candidates rather than an
 *  empty "No suggestions". Typing switches to a live text search over the same endpoint; no
 *  match there offers "Add new tag" to grow the vocabulary on the spot. Built generic so it
 *  is not tied to the merge flow - anywhere a tag needs picking from a vocabulary that can
 *  run past a hundred entries can reuse it.
 *
 *  `chip`: collapsed to a `.tag-action-chip` pill (just `chipLabel` + a caret) until clicked -
 *  same shape as `TagMergeOptions`'s always-expanded merge chip, so the two "Merge into" looks
 *  do not clash next to each other. Clicking it opens the same floating dropdown (input +
 *  suggestion/spelling/search panel) the plain picker always shows; picking a tag, Escape or a
 *  click outside collapse it back to the chip. */
export function TagPicker({
  source,
  suggestion,
  excludeIds = [],
  onPick,
  disabled = false,
  placeholder = 'Merge into…',
  ariaLabel,
  chip = false,
  chipLabel = 'Merge into',
  chipIconOnly = false,
}: {
  source?: Tag
  suggestion?: Tag
  /** Extra tag ids to leave out of both lists, beyond `source` itself - e.g. its current parents. */
  excludeIds?: string[]
  onPick: (tag: Tag) => void
  disabled?: boolean
  placeholder?: string
  ariaLabel: string
  chip?: boolean
  chipLabel?: string
  chipIconOnly?: boolean
}) {
  const {
    text,
    spelling,
    spellingIsFallback,
    results,
    creating,
    createError,
    term,
    pick,
    addNew,
    setOpen,
    updateText,
  } = useTagPicker({ source, suggestion, excludeIds, onPick })

  const options = (
    <>
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
                <p className="tag-picker-heading">{source && !spellingIsFallback ? 'Similar spelling' : 'Tags'}</p>
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
    </>
  )

  if (chip) {
    return (
      <Dropdown
        className="tag-picker tag-picker-chip-anchor"
        panelClassName="tag-picker-panel"
        align="start"
        onOpenChange={setOpen}
        trigger={({ isOpen, toggle }) => (
          <button
            type="button"
            className={chipIconOnly ? 'tag-action-chip tag-action-chip-merge tag-picker-chip tag-picker-chip-icon' : 'tag-action-chip tag-action-chip-merge tag-picker-chip'}
            aria-label={ariaLabel}
            aria-expanded={isOpen}
            disabled={disabled}
            onClick={toggle}
          >
            {!chipIconOnly && <span className="tag-action-label">{chipLabel}</span>}
            <span className="tag-picker-chevron" aria-hidden="true">▾</span>
          </button>
        )}
      >
        {() => (
          <>
            <input
              type="text"
              className="field field-xs tag-picker-input"
              value={text}
              placeholder={placeholder}
              disabled={disabled}
              aria-label={ariaLabel}
              autoFocus
              onChange={(event) => updateText(event.target.value)}
            />
            {options}
          </>
        )}
      </Dropdown>
    )
  }

  return (
    <Dropdown
      className="tag-picker"
      panelClassName="tag-picker-panel"
      align="start"
      onOpenChange={setOpen}
      trigger={({ open: openDropdown }) => (
        <input
          type="text"
          className="field field-xs tag-picker-input"
          value={text}
          placeholder={placeholder}
          disabled={disabled}
          aria-label={ariaLabel}
          onFocus={openDropdown}
          onChange={(event) => {
            updateText(event.target.value)
            openDropdown()
          }}
        />
      )}
    >
      {() => (
        <>
          {options}
        </>
      )}
    </Dropdown>
  )
}
