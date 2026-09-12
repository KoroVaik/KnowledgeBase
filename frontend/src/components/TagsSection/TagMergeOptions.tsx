import type { Tag } from '../../api/tags'
import { useTagMergeOptions } from './useTagMergeOptions'

/** Replaces the searchable "Merge into…" picker for an unconfirmed tag that has an AI
 *  suggestion: one button per candidate (suggestion + similar spelling), so the common case
 *  is a single click instead of open-picker-then-pick. Tags with no suggestion still fall
 *  back to `TagPicker` in `TagsSection` - there is nothing to make explicit yet. */
export function TagMergeOptions({
  tag,
  suggestion,
  excludeIds = [],
  disabled = false,
  onPick,
}: {
  tag: Tag
  suggestion: Tag
  excludeIds?: string[]
  disabled?: boolean
  onPick: (tag: Tag) => void
}) {
  const { candidates } = useTagMergeOptions(tag, suggestion, excludeIds)

  return (
    <>
      {candidates.map((candidate) => (
        <button
          key={candidate.id}
          type="button"
          className="btn btn-xs"
          title={`Merge into "${candidate.name}"`}
          disabled={disabled}
          onClick={() => onPick(candidate)}
        >
          → {candidate.name}
        </button>
      ))}
    </>
  )
}
