import type { TagSuggestion } from '../../api/tags'

export interface SuggestionCandidate extends TagSuggestion {
  confirmed: boolean
}

/** One "Parent"/"Child" pill naming the action, AI-suggested candidates inside as accept/reject
 *  pairs - the flat-list counterpart of TagSuggestionGraph's parent/child tiers, same shape
 *  whether the candidates are an unconfirmed tag's own suggested parents or a confirmed tag's
 *  pending placement suggestion. */
export function TagSuggestionChip({
  label,
  direction,
  candidates,
  disabled,
  onAccept,
  onReject,
}: {
  label: string
  direction: 'parent' | 'child'
  candidates: SuggestionCandidate[]
  disabled: boolean
  onAccept: (id: string) => void
  onReject: (id: string) => void
}) {
  if (candidates.length === 0) {
    return null
  }

  return (
    <span className={`tag-action-chip tag-action-chip-${direction}`}>
      <span className="tag-action-label">{label}</span>
      {candidates.map((candidate) => (
        <span key={candidate.id} className="tag-suggestion-candidate">
          <span
            className={
              candidate.confirmed
                ? 'tag-suggestion-candidate-pill tag-suggestion-candidate-confirmed'
                : 'tag-suggestion-candidate-pill tag-suggestion-candidate-unconfirmed'
            }
          >
            {candidate.name}
            <span className="tag-confidence">{candidate.confidence.toLowerCase()}</span>
          </span>
          <button
            type="button"
            className="tag-suggestion-accept"
            aria-label={`Accept ${candidate.name}`}
            onClick={() => onAccept(candidate.id)}
            disabled={disabled}
          >
            ✓
          </button>
          <button
            type="button"
            className="tag-suggestion-reject"
            aria-label={`Reject ${candidate.name}`}
            onClick={() => onReject(candidate.id)}
            disabled={disabled}
          >
            ×
          </button>
        </span>
      ))}
    </span>
  )
}
