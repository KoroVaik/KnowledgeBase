import { useEffect, useState } from 'react'
import type { SuggestionDecision } from './suggestionDecisions'

/** Local, unsent accept/reject/flip choices for one row's suggestion candidates, keyed
 *  `${role}:${id}` (see suggestionDecisions.ts) - nothing here reaches the backend until the row's
 *  Confirm/Apply button submits it, so a row survives a background reload of the tags list without
 *  losing what the user already decided (see "One review queue, not two" follow-up in
 *  docs/frontend.md).
 *
 *  `requiredKeys` is a fresh array every render of the caller, so it is joined into a stable
 *  string for the effect dependency - same trick as useTagPlacementSuggestions.ts's idsKey. */
export function useSuggestionDecisions(requiredKeys: string[]) {
  const [decisions, setDecisions] = useState<Map<string, SuggestionDecision>>(new Map())
  const keysJoined = requiredKeys.join(',')

  // A candidate that disappears from a fresh fetch (resolved elsewhere, or expired) has its
  // staged decision quietly dropped along with it - there is nothing left to submit it against.
  useEffect(() => {
    setDecisions((current) => {
      let changed = false
      const next = new Map(current)
      for (const key of next.keys()) {
        if (!requiredKeys.includes(key)) {
          next.delete(key)
          changed = true
        }
      }
      return changed ? next : current
    })
    // oxlint-disable-next-line react-hooks/exhaustive-deps -- keysJoined stands in for requiredKeys, see above
  }, [keysJoined])

  function decide(key: string, decision: SuggestionDecision) {
    setDecisions((current) => {
      const next = new Map(current)
      if (next.get(key) === decision) {
        next.delete(key)
      } else {
        next.set(key, decision)
      }
      return next
    })
  }

  const allDecided = requiredKeys.every((key) => decisions.has(key))
  const hasAccepted = requiredKeys.some((key) => {
    const decision = decisions.get(key)
    return decision === 'accept' || decision === 'flip-accept'
  })

  return { decisions, decide, allDecided, hasAccepted }
}
