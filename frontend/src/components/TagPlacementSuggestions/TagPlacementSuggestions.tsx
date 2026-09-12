import type { Tag, TagSuggestion } from '../../api/tags'
import { useTagPlacementSuggestions } from './useTagPlacementSuggestions'
import { TagSuggestionGraph } from '../TagSuggestionGraph/TagSuggestionGraph'
import { TagSuggestionChip } from './TagSuggestionChip'
import { TagPicker } from '../TagPicker/TagPicker'
import './TagPlacementSuggestions.css'

/** Rows for confirmed tags with a pending AI placement guess - `<li>`s meant to sit inside the
 *  same `.tags-list` as the "To review" rows, not a block of its own: same tag chip, merge
 *  control and accept/reject shape as an unconfirmed tag's own suggestion row in
 *  `TagsSection.tsx` (`TagSuggestionChip`/`TagSuggestionGraph`, shared by both so the two cannot
 *  drift apart again), just sourced from a fetch instead of riding along on the tag itself
 *  (a confirmed tag can gain several parents over time, see "Tag hierarchy" in
 *  docs/database.md). `view` switches the same accept/reject data to `TagSuggestionGraph`'s mini
 *  node-link presentation instead - no change to the suggestions themselves. */
export function TagPlacementSuggestions({
  tags,
  tagsById,
  view,
  disabled,
  onAccept,
  onReject,
  onFlip,
  onMerge,
}: {
  tags: Tag[]
  // Confirmed status per candidate is not part of the suggestion payload itself (see
  // TagSuggestionResponse) - the pool a placement is drawn from was widened to include
  // not-yet-reviewed tags too, so a candidate here can be either. TagsSection already has every
  // tag loaded, so this is a lookup rather than a second fetch.
  tagsById: Map<string, Tag>
  view: 'list' | 'graph'
  disabled: boolean
  onAccept: (childId: string, parentId: string) => void
  onReject: (childId: string, parentId: string) => void
  // Commits a Flip: creates the link with the ids in the opposite roles the AI proposed
  // (newChildId's real parent becomes newParentId) and dismisses the original guess.
  onFlip: (newChildId: string, newParentId: string) => void
  onMerge: (tag: Tag, into: Tag) => void
}) {
  const taggedIds = tags.map((tag) => tag.id)
  const { byId, error } = useTagPlacementSuggestions(taggedIds)

  const withConfirmed = (candidates: TagSuggestion[]) =>
    candidates.map((candidate) => ({ ...candidate, confirmed: tagsById.get(candidate.id)?.confirmed ?? false }))

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

        const mergeControl = (
          <TagPicker
            source={tag}
            ariaLabel={`Merge ${tag.name} into another tag`}
            disabled={disabled}
            onPick={(into) => onMerge(tag, into)}
            chip
          />
        )

        const suggestedParents = withConfirmed(suggestions.suggestedParents)
        const suggestedChildren = withConfirmed(suggestions.suggestedChildren)

        if (view === 'graph') {
          // Flip only offered when this row has exactly one candidate total - with a candidate
          // on both tiers (or several on one), swapping the center would leave the others'
          // edges pointing at a center that just changed identity.
          const totalCandidates = suggestedParents.length + suggestedChildren.length
          const soleParent = totalCandidates === 1 ? suggestedParents[0] : undefined
          const soleChild = totalCandidates === 1 ? suggestedChildren[0] : undefined

          return (
            <li key={tag.id} className="tags-row">
              <TagSuggestionGraph
                centerName={tag.name}
                centerConfirmed
                parents={suggestedParents}
                childCandidates={suggestedChildren}
                disabled={disabled}
                onAcceptParent={(parentId) => onAccept(tag.id, parentId)}
                onRejectParent={(parentId) => onReject(tag.id, parentId)}
                onAcceptChild={(childId) => onAccept(childId, tag.id)}
                onRejectChild={(childId) => onReject(childId, tag.id)}
                onFlipAcceptParent={soleParent !== undefined ? () => onFlip(soleParent.id, tag.id) : undefined}
                onFlipAcceptChild={soleChild !== undefined ? () => onFlip(tag.id, soleChild.id) : undefined}
              />

              <span className="tags-actions">{mergeControl}</span>
            </li>
          )
        }

        return (
          <li key={tag.id} className="tags-row">
            <span className="tag-chip chip-compact">{tag.name}</span>

            <TagSuggestionChip
              label="Parent"
              direction="parent"
              candidates={suggestedParents}
              disabled={disabled}
              onAccept={(parentId) => onAccept(tag.id, parentId)}
              onReject={(parentId) => onReject(tag.id, parentId)}
            />

            <TagSuggestionChip
              label="Child"
              direction="child"
              candidates={suggestedChildren}
              disabled={disabled}
              onAccept={(childId) => onAccept(childId, tag.id)}
              onReject={(childId) => onReject(childId, tag.id)}
            />

            <span className="tags-actions">{mergeControl}</span>
          </li>
        )
      })}
    </>
  )
}
