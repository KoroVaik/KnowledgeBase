import type { Tag } from '../../api/tags'
import type { MiniGraphCandidate } from '../TagSuggestionGraph/TagSuggestionGraph'
import { applySuggestionDecision } from '../TagSuggestionGraph/suggestionDecisions'
import { useSuggestionDecisions } from '../TagSuggestionGraph/useSuggestionDecisions'

// A tag with no pending parent suggestion still gets a mini-graph row - this fills the parent
// slot so the layout has one candidate to size and position, instead of a flat chip fallback.
const NO_PARENT_SUGGESTION: MiniGraphCandidate = {
  id: '__no-parent-suggestion__',
  name: 'No suggested parent found',
  confidence: '',
  confirmed: false,
  placeholder: true,
}

/** State and handlers behind one "To review" row: stages accept/reject/flip against the tag's
 *  own pending parent suggestions and turns them into the row's single submit button - see
 *  TagReviewRow.tsx and "One review queue, not two" in docs/frontend.md for why this replaced the
 *  separate always-visible Confirm button. */
export function useTagReviewRow(
  tag: Tag,
  tagsById: Map<string, Tag>,
  onSubmit: (tag: Tag, actions: Array<() => Promise<void>>) => void,
) {
  const parentCandidates = tag.pendingParentSuggestions.flatMap((pending) => {
    const parent = tagsById.get(pending.parentId)
    return parent === undefined ? [] : [{ parent, confidence: pending.confidence }]
  })

  const requiredKeys = parentCandidates.map(({ parent }) => `parent:${parent.id}`)
  const { decisions, decide, allDecided, hasAccepted } = useSuggestionDecisions(requiredKeys)

  const candidates: MiniGraphCandidate[] =
    parentCandidates.length > 0
      ? parentCandidates.map(({ parent, confidence }) => ({
          id: parent.id,
          name: parent.name,
          confidence,
          confirmed: parent.confirmed,
          decision: decisions.get(`parent:${parent.id}`),
        }))
      : [NO_PARENT_SUGGESTION]

  const onAcceptParent = (parentId: string) => decide(`parent:${parentId}`, 'accept')
  const onRejectParent = (parentId: string) => decide(`parent:${parentId}`, 'reject')

  // Flip only offered when this row has exactly one candidate - with more, swapping the center
  // would leave the other candidates' edges pointing at a center that just changed identity.
  const sole = parentCandidates.length === 1 ? parentCandidates[0] : null
  const onFlipAcceptParent =
    sole !== null ? () => decide(`parent:${sole.parent.id}`, 'flip-accept') : undefined

  function submit() {
    const actions = parentCandidates.flatMap(({ parent }) => {
      const decision = decisions.get(`parent:${parent.id}`)
      return decision === undefined
        ? []
        : [applySuggestionDecision(tag.id, { id: parent.id, role: 'parent' }, decision)]
    })
    onSubmit(tag, actions)
  }

  return { candidates, onAcceptParent, onRejectParent, onFlipAcceptParent, hasAccepted, allDecided, submit }
}
