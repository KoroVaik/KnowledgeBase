import type { Tag } from '../../api/tags'
import { notesText } from '../../format'
import { TagSuggestionGraph } from '../TagSuggestionGraph/TagSuggestionGraph'
import { TagPicker } from '../TagPicker/TagPicker'
import { useTagReviewRow } from './useTagReviewRow'

const MIN_NOTES = 2

/** One unconfirmed tag in "To review": the mini-graph plus its own submit button, right of the
 *  graph so it reads as "what happens to this row" rather than a page-wide action (see
 *  tags-row-submit in TagsSection.css). Accept/reject/flip on the graph only stage a decision
 *  locally (useTagReviewRow) - nothing reaches the backend until this button is clicked. */
export function TagReviewRow({
  tag,
  tagsById,
  disabled,
  queued,
  onMerge,
  onSynthesise,
  onDelete,
  onSubmit,
}: {
  tag: Tag
  tagsById: Map<string, Tag>
  disabled: boolean
  queued: string[]
  onMerge: (tag: Tag, into: Tag) => void
  onSynthesise: (tag: Tag) => void
  onDelete: (tag: Tag) => void
  onSubmit: (tag: Tag, actions: Array<() => Promise<void>>) => void
}) {
  const { candidates, onAcceptParent, onRejectParent, onFlipAcceptParent, hasAccepted, allDecided, submit } =
    useTagReviewRow(tag, tagsById, onSubmit)

  const suggestion = tag.suggestedMergeIntoId !== null ? tagsById.get(tag.suggestedMergeIntoId) : undefined

  return (
    <li className="tags-row">
      <TagSuggestionGraph
        centerName={tag.name}
        centerConfirmed={false}
        parents={candidates}
        childCandidates={[]}
        disabled={disabled}
        onAcceptParent={onAcceptParent}
        onRejectParent={onRejectParent}
        onFlipAcceptParent={onFlipAcceptParent}
      />

      <button
        type="button"
        className={allDecided ? 'btn btn-xs btn-primary tags-row-submit' : 'btn btn-xs tags-row-submit'}
        onClick={submit}
        disabled={disabled || !allDecided}
      >
        Submit new tag &quot;<span className="tags-row-submit-name">{tag.name}</span>&quot;{' '}
        {hasAccepted ? 'with parent(s)' : 'without parent'}
      </button>

      <span className="tags-count">{notesText(tag.noteCount)}</span>

      <span className="tags-actions">
        <TagPicker
          source={tag}
          suggestion={suggestion}
          ariaLabel={`Merge ${tag.name} into another tag`}
          disabled={disabled}
          onPick={(into) => onMerge(tag, into)}
          chip
        />

        {tag.noteCount >= MIN_NOTES && (
          <button type="button" className="btn btn-xs" onClick={() => onSynthesise(tag)} disabled={disabled}>
            {queued.includes(tag.name) ? 'Queued' : 'Synthesise'}
          </button>
        )}

        <button type="button" className="btn btn-xs" onClick={() => onDelete(tag)} disabled={disabled}>
          Delete
        </button>
      </span>
    </li>
  )
}
