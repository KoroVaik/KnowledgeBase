import { useEffect, useRef, useState } from 'react'
import { fetchTagParentSuggestions } from '../../api/tags'
import type { TagParentSuggestions } from '../../api/tags'

/** Fetches the placement-suggestion detail (names, not just the flag on Tag) for every tag id
 *  passed in. Re-fetches the whole set whenever *which* ids are flagged changes - accepting or
 *  rejecting one changes that tag's own row, so there is no cache worth keeping across an
 *  action. */
export function useTagPlacementSuggestions(taggedIds: string[], revision = 0) {
  const [byId, setById] = useState<Map<string, TagParentSuggestions>>(new Map())
  const [error, setError] = useState<string | null>(null)
  const latest = useRef(0)
  const appliedRevision = useRef(revision)

  // A stable dependency: taggedIds is a fresh array on every render of the caller.
  const idsKey = taggedIds.join(',')

  useEffect(() => {
    if (taggedIds.length === 0) {
      setById(new Map())
      return
    }

    const requestId = ++latest.current

    void Promise.all(
      taggedIds.map((id) => fetchTagParentSuggestions(id).then((suggestions) => [id, suggestions] as const)),
    )
      .then((entries) => {
        if (requestId !== latest.current) {
          return
        }

        const replace = appliedRevision.current !== revision
        appliedRevision.current = revision
        setById(current => new Map(entries.map(([id, value]) => [id, replace ? value : current.get(id) ?? value])))
        setError(null)
      })
      .catch((err: unknown) => {
        if (requestId !== latest.current) {
          return
        }

        setError(err instanceof Error ? err.message : 'Unexpected error')
      })
    // oxlint-disable-next-line react-hooks/exhaustive-deps -- idsKey is the stable stand-in for taggedIds, see above
  }, [idsKey, revision])

  return { byId, error }
}
