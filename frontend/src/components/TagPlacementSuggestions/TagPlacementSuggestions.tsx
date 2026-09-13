import type { Tag } from '../../api/tags'
import { useTagPlacementSuggestions } from './useTagPlacementSuggestions'
import { TagPlacementRow } from './TagPlacementRow'

/** Rows for confirmed tags with a pending AI placement guess - `<li>`s meant to sit inside the
 *  same `.tags-list` as the "To review" rows, not a block of its own: same tag chip, merge
 *  control and accept/reject shape as an unconfirmed tag's own suggestion row in
 *  `TagsSection.tsx` (`TagSuggestionGraph`, shared by both so the two cannot drift apart again),
 *  just sourced from a fetch instead of riding along on the tag itself (a confirmed tag can gain
 *  several parents over time, see "Tag hierarchy" in docs/database.md). A row here always has at
 *  least one suggestion (see the early `return null` below), so it never needs the placeholder
 *  node `TagsSection.tsx`'s own rows fall back to. */
export function TagPlacementSuggestions({
  tags,
  tagsById,
  disabled,
  onMerge,
  onSubmit,
}: {
  tags: Tag[]
  // Confirmed status per candidate is not part of the suggestion payload itself (see
  // TagSuggestionResponse) - the pool a placement is drawn from was widened to include
  // not-yet-reviewed tags too, so a candidate here can be either. TagsSection already has every
  // tag loaded, so this is a lookup rather than a second fetch.
  tagsById: Map<string, Tag>
  disabled: boolean
  onMerge: (tag: Tag, into: Tag) => void
  onSubmit: (tag: Tag, actions: Array<() => Promise<void>>) => void
}) {
  const taggedIds = tags.map((tag) => tag.id)
  const { byId, error } = useTagPlacementSuggestions(taggedIds)

  return (
    <>
      {error !== null && (
        <li className="tags-row">
          <p className="notes-error" role="alert">
            {error}
          </p>
        </li>
      )}

      {tags.map((tag) => {
        const suggestions = byId.get(tag.id)

        if (!suggestions || (suggestions.suggestedParents.length === 0 && suggestions.suggestedChildren.length === 0)) {
          return null
        }

        return (
          <TagPlacementRow
            key={tag.id}
            tag={tag}
            suggestions={suggestions}
            tagsById={tagsById}
            disabled={disabled}
            onMerge={onMerge}
            onSubmit={onSubmit}
          />
        )
      })}
    </>
  )
}
