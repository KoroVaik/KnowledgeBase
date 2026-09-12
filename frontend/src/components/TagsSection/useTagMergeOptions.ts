import { useEffect, useState } from 'react'
import { fetchTags } from '../../api/tags'
import type { Tag } from '../../api/tags'

const SPELLING_LIMIT = 5

/** Explicit merge candidates for one unconfirmed tag: the AI's `suggestedMergeIntoId` first,
 *  then confirmed tags with a name close to the source's (same ranked search TagPicker uses
 *  for its closed-input "Similar spelling" list) - fetched once, not behind a search box. */
export function useTagMergeOptions(tag: Tag, suggestion: Tag, excludeIds: string[]) {
  const [spelling, setSpelling] = useState<Tag[]>([])
  const excludeKey = excludeIds.join(',')

  useEffect(() => {
    let cancelled = false

    void fetchTags({ query: tag.name, excludeId: tag.id }).then((tags) => {
      if (cancelled) {
        return
      }

      setSpelling(
        tags.filter((candidate) => candidate.id !== suggestion.id && !excludeIds.includes(candidate.id)).slice(0, SPELLING_LIMIT),
      )
    })

    return () => {
      cancelled = true
    }
    // oxlint-disable-next-line react-hooks/exhaustive-deps -- excludeKey is the stable stand-in for excludeIds
  }, [tag.id, tag.name, suggestion.id, excludeKey])

  return { candidates: [suggestion, ...spelling] }
}
