import type { Tag, TagParentSuggestions } from '../../api/tags'
import { TagSuggestionGraph } from '../TagSuggestionGraph/TagSuggestionGraph'
import { TagSearchPicker } from '../TagSearchPicker/TagSearchPicker'
import { useTagPlacementRow } from './useTagPlacementRow'

/** One confirmed tag with a pending placement guess. Same staged-decision shape as
 *  TagsSection/TagReviewRow.tsx, just no tag-confirm step and a static "Apply" label instead of
 *  the dynamic "Submit new tag …" one - the tag itself needs no confirming here. */
export function TagPlacementRow({
  tag,
  suggestions,
  tagsById,
  disabled,
  onMerge,
  onSubmit,
}: {
  tag: Tag
  suggestions: TagParentSuggestions
  tagsById: Map<string, Tag>
  disabled: boolean
  onMerge: (tag: Tag, into: Tag) => void
  onSubmit: (tag: Tag, actions: Array<() => Promise<void>>) => void
}) {
  const {
    parents,
    childCandidates,
    onAcceptParent,
    onRejectParent,
    onAcceptChild,
    onRejectChild,
    onFlipAcceptParent,
    onFlipAcceptChild,
    allDecided,
    submit,
  } = useTagPlacementRow(tag, suggestions, tagsById, onSubmit)

  return (
    <li className="tags-row">
      <TagSuggestionGraph
        centerName={tag.name}
        centerConfirmed
        parents={parents}
        childCandidates={childCandidates}
        disabled={disabled}
        onAcceptParent={onAcceptParent}
        onRejectParent={onRejectParent}
        onAcceptChild={onAcceptChild}
        onRejectChild={onRejectChild}
        onFlipAcceptParent={onFlipAcceptParent}
        onFlipAcceptChild={onFlipAcceptChild}
      />

      <button
        type="button"
        className={allDecided ? 'btn btn-xs btn-primary tags-row-submit' : 'btn btn-xs tags-row-submit'}
        onClick={submit}
        disabled={disabled || !allDecided}
      >
        Apply
      </button>

      <span className="tags-actions">
        <TagSearchPicker
          source={tag}
          ariaLabel={`Merge ${tag.name} into another tag`}
          disabled={disabled}
          onPick={(into) => onMerge(tag, into)}
          chip
        />
      </span>
    </li>
  )
}
