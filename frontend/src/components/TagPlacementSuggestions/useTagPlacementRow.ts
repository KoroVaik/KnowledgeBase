import type { Tag, TagParentSuggestions } from '../../api/tags'
import type { MiniGraphCandidate } from '../TagSuggestionGraph/TagSuggestionGraph'
import { applySuggestionDecision } from '../TagSuggestionGraph/suggestionDecisions'
import { useSuggestionDecisions } from '../TagSuggestionGraph/useSuggestionDecisions'

/** State and handlers behind one "To place" row (a confirmed tag with a pending placement
 *  guess): stages accept/reject/flip on either tier into one Apply button - see
 *  TagPlacementRow.tsx. No confirm step here (the tag already is confirmed), unlike
 *  useTagReviewRow.ts's submit. */
export function useTagPlacementRow(
  tag: Tag,
  suggestions: TagParentSuggestions,
  tagsById: Map<string, Tag>,
  onSubmit: (tag: Tag, actions: Array<() => Promise<void>>) => void,
) {
  const withConfirmed = (candidates: TagParentSuggestions['suggestedParents']) =>
    candidates.map((candidate) => ({ ...candidate, confirmed: tagsById.get(candidate.id)?.confirmed ?? false }))

  const suggestedParents = withConfirmed(suggestions.suggestedParents)
  const suggestedChildren = withConfirmed(suggestions.suggestedChildren)

  const requiredKeys = [
    ...suggestedParents.map((candidate) => `parent:${candidate.id}`),
    ...suggestedChildren.map((candidate) => `child:${candidate.id}`),
  ]
  const { decisions, decide, allDecided } = useSuggestionDecisions(requiredKeys)

  const parents: MiniGraphCandidate[] = suggestedParents.map((candidate) => ({
    ...candidate,
    decision: decisions.get(`parent:${candidate.id}`),
  }))
  const childCandidates: MiniGraphCandidate[] = suggestedChildren.map((candidate) => ({
    ...candidate,
    decision: decisions.get(`child:${candidate.id}`),
  }))

  // Flip only offered when this row has exactly one candidate total - with a candidate on both
  // tiers (or several on one), swapping the center would leave the others' edges pointing at a
  // center that just changed identity.
  const totalCandidates = suggestedParents.length + suggestedChildren.length
  const soleParent = totalCandidates === 1 ? suggestedParents[0] : undefined
  const soleChild = totalCandidates === 1 ? suggestedChildren[0] : undefined

  const onAcceptParent = (id: string) => decide(`parent:${id}`, 'accept')
  const onRejectParent = (id: string) => decide(`parent:${id}`, 'reject')
  const onAcceptChild = (id: string) => decide(`child:${id}`, 'accept')
  const onRejectChild = (id: string) => decide(`child:${id}`, 'reject')
  const onFlipAcceptParent = soleParent !== undefined ? () => decide(`parent:${soleParent.id}`, 'flip-accept') : undefined
  const onFlipAcceptChild = soleChild !== undefined ? () => decide(`child:${soleChild.id}`, 'flip-accept') : undefined

  function submit() {
    const actions = [
      ...suggestedParents.flatMap((candidate) => {
        const decision = decisions.get(`parent:${candidate.id}`)
        return decision === undefined
          ? []
          : [applySuggestionDecision(tag.id, { id: candidate.id, role: 'parent' }, decision)]
      }),
      ...suggestedChildren.flatMap((candidate) => {
        const decision = decisions.get(`child:${candidate.id}`)
        return decision === undefined
          ? []
          : [applySuggestionDecision(tag.id, { id: candidate.id, role: 'child' }, decision)]
      }),
    ]
    onSubmit(tag, actions)
  }

  return {
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
  }
}
