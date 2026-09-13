import { acceptTagParentSuggestion, addTagParent, rejectTagParentSuggestion } from '../../api/tags'

/** A candidate's outcome as staged locally, before it is sent to the backend - `flip-accept` is
 *  Flip's commit (see TagSuggestionGraph.tsx), same acceptance just with the parent/child roles
 *  reversed from what the model proposed. */
export type SuggestionDecision = 'accept' | 'reject' | 'flip-accept'

export interface SuggestionCandidateRef {
  id: string
  /** Which tier the candidate sits in relative to the tag being decided - a suggested parent or
   *  a suggested child. */
  role: 'parent' | 'child'
}

/** Turns one staged decision into the API call(s) it commits to. The backend matches a
 *  parent-suggestion lookup in either id order (TagsController.FindSuggestionAsync), so accept/
 *  reject do not need to care which side is which beyond building the right pair of ids. */
export function applySuggestionDecision(
  tagId: string,
  candidate: SuggestionCandidateRef,
  decision: SuggestionDecision,
): () => Promise<void> {
  const { id: candidateId, role } = candidate
  // The direction the suggestion was proposed in - "parent" means the candidate is tag's
  // suggested parent, "child" the reverse.
  const origChildId = role === 'parent' ? tagId : candidateId
  const origParentId = role === 'parent' ? candidateId : tagId

  if (decision === 'accept') {
    return () => acceptTagParentSuggestion(origChildId, origParentId)
  }

  if (decision === 'reject') {
    return () => rejectTagParentSuggestion(origChildId, origParentId)
  }

  // Flip: commit the opposite direction from what the model proposed, then dismiss the
  // original guess (still looked up in its original direction).
  return async () => {
    await addTagParent(origParentId, origChildId)
    await rejectTagParentSuggestion(origChildId, origParentId)
  }
}
