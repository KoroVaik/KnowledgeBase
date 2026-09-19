import type { Tag } from '../../api/tags'
import { SearchPicker, type SearchPickerSection } from '../SearchPicker/SearchPicker'
import { useTagSearch } from './useTagSearch'

/** SearchPicker over the tag vocabulary. Closed input opens two ranked sections: the pipeline's semantic
 *  `suggestion` first, then tags with a name close to `source`'s (backend rank, see TagsController.List) -
 *  or, when nothing shares spelling with `source`'s name, the plain busiest-tags listing instead, so the
 *  panel always offers real candidates. Typing switches to a live text search; no match there offers
 *  "Add new tag" to grow the vocabulary on the spot.
 *
 *  `chip`: the collapsed pill uses the tag-action chip look, so it sits beside `TagMergeOptions`'
 *  always-expanded merge chip without clashing. */
export function TagSearchPicker({
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
  const { text, spelling, spellingIsFallback, results, creating, createError, term, pick, addNew, setOpen, updateText } =
    useTagSearch({ source, suggestion, excludeIds, onPick })

  const sections: SearchPickerSection<Tag>[] = term.length === 0
    ? [
        { heading: 'Suggested', items: suggestion === undefined ? [] : [suggestion], accent: true },
        { heading: source && !spellingIsFallback ? 'Similar spelling' : 'Tags', items: spelling },
      ]
    : [{ items: results }]

  return <SearchPicker
    text={text}
    onTextChange={updateText}
    onOpenChange={setOpen}
    sections={sections}
    add={term.length > 0 && results.length === 0 ? { label: creating ? 'Adding…' : `Add new tag “${term}”`, busy: creating, onAdd: () => void addNew() } : null}
    error={createError}
    emptyMessage={term.length === 0 ? 'No suggestions — start typing to search.' : undefined}
    getKey={tag => tag.id}
    getLabel={tag => tag.name}
    onPick={pick}
    disabled={disabled}
    placeholder={placeholder}
    ariaLabel={ariaLabel}
    chip={chip}
    chipLabel={chipLabel}
    chipIconOnly={chipIconOnly}
    chipClassName="tag-action-chip tag-action-chip-merge"
    chipLabelClassName="tag-action-label"
  />
}
