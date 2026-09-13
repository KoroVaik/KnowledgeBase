import type { Tag, TagParentSuggestions } from '../../api/tags'
import { TagPlacementRow } from './TagPlacementRow'

export interface Placement {
  tag: Tag
  suggestions: TagParentSuggestions
}

/** Rows for confirmed tags with a pending AI placement guess - `<li>`s meant to sit inside the
 *  same `.tags-list` as the "To review" rows, not a block of its own: same tag chip, merge
 *  control and accept/reject shape as an unconfirmed tag's own suggestion row in
 *  `TagsSection.tsx` (`TagSuggestionGraph`, shared by both so the two cannot drift apart again),
 *  just sourced from a fetch instead of riding along on the tag itself (a confirmed tag can gain
 *  several parents over time, see "Tag hierarchy" in docs/database.md). `TagsSection` passes only
 *  placements with at least one suggestion left, so a row never needs a placeholder node. */
export function TagPlacementSuggestions({
  placements,
  error,
  tagsById,
  disabled,
  onMerge,
  onSubmit,
}: {
  placements: Placement[]
  error: string | null
  // Confirmed status per candidate is not part of the suggestion payload itself (see
  // TagSuggestionResponse) - the pool a placement is drawn from was widened to include
  // not-yet-reviewed tags too, so a candidate here can be either. TagsSection already has every
  // tag loaded, so this is a lookup rather than a second fetch.
  tagsById: Map<string, Tag>
  disabled: boolean
  onMerge: (tag: Tag, into: Tag) => void
  onSubmit: (tag: Tag, actions: Array<() => Promise<void>>) => void
}) {
  return (
    <>
      {error !== null && (
        <li className="tags-row">
          <p className="notes-error" role="alert">
            {error}
          </p>
        </li>
      )}

      {placements.map(({ tag, suggestions }) => (
        <TagPlacementRow
          key={tag.id}
          tag={tag}
          suggestions={suggestions}
          tagsById={tagsById}
          disabled={disabled}
          onMerge={onMerge}
          onSubmit={onSubmit}
        />
      ))}
    </>
  )
}
