import { useEffect, useRef, useState } from 'react'
import { createTag, fetchTags } from '../../api/tags'
import type { Tag } from '../../api/tags'

const DEBOUNCE_MS = 200
const SEARCH_LIMIT = 8
const SPELLING_LIMIT = 5

interface UseTagPickerOptions {
  /** The tag this picker is choosing a match *for* (merge target search, spelling-close
   *  suggestions on open). Omit for a plain "pick any tag" use, e.g. adding one to a note. */
  source?: Tag
  suggestion?: Tag
  /** Extra tag ids to leave out of both lists, beyond `source` itself - e.g. its current parents. */
  excludeIds: string[]
  onPick: (tag: Tag) => void
}

/** State behind TagPicker: the open panel's two ranked sections (suggestion + similar
 *  spelling, closed input) or a live text search, plus "Add new tag" when nothing matches. */
export function useTagPicker({ source, suggestion, excludeIds, onPick }: UseTagPickerOptions) {
  const [text, setText] = useState('')
  const [open, setOpen] = useState(false)
  const [spelling, setSpelling] = useState<Tag[]>([])
  const [spellingIsFallback, setSpellingIsFallback] = useState(false)
  const [results, setResults] = useState<Tag[]>([])
  const [creating, setCreating] = useState(false)
  const [createError, setCreateError] = useState<string | null>(null)
  const containerRef = useRef<HTMLDivElement>(null)

  const term = text.trim()
  // A stable dependency: excludeIds is a fresh array on every render (callers often pass a
  // literal), which would re-run the effect - and re-debounce - on every keystroke.
  const excludeKey = excludeIds.join(',')

  useEffect(() => {
    if (!open) {
      return
    }

    const handle = setTimeout(() => {
      if (term.length === 0) {
        // With no source there is no name to seed a spelling-close search from - list the
        // busiest tags instead, same as the vocabulary browser with no query.
        const listing = source === undefined ? fetchTags() : fetchTags({ query: source.name, excludeId: source.id })

        void listing.then((tags) => {
          const close = tags.filter((tag) => tag.id !== suggestion?.id && !excludeIds.includes(tag.id))

          if (close.length > 0 || source === undefined) {
            setSpellingIsFallback(false)
            setSpelling(close.slice(0, SPELLING_LIMIT))
            return
          }

          // Nothing shares spelling with source's name - fall back to the busiest-tags listing
          // rather than leaving the panel empty; at least offers real candidates to pick from.
          void fetchTags({ excludeId: source.id }).then((allTags) => {
            setSpellingIsFallback(true)
            setSpelling(
              allTags.filter((tag) => tag.id !== suggestion?.id && !excludeIds.includes(tag.id)).slice(0, SPELLING_LIMIT),
            )
          })
        })
      } else {
        void fetchTags({ query: term, excludeId: source?.id }).then((tags) =>
          setResults(tags.filter((tag) => !excludeIds.includes(tag.id)).slice(0, SEARCH_LIMIT)),
        )
      }
    }, DEBOUNCE_MS)

    return () => clearTimeout(handle)
    // oxlint-disable-next-line react-hooks/exhaustive-deps -- excludeKey is the stable stand-in for excludeIds, see above
  }, [term, source?.id, source?.name, suggestion?.id, excludeKey, open])

  useEffect(() => {
    function onClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    document.addEventListener('mousedown', onClickOutside)
    return () => document.removeEventListener('mousedown', onClickOutside)
  }, [])

  function pick(tag: Tag) {
    onPick(tag)
    setText('')
    setOpen(false)
  }

  async function addNew() {
    setCreateError(null)
    setCreating(true)

    try {
      pick(await createTag(term))
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : 'Unexpected error')
    } finally {
      setCreating(false)
    }
  }

  function focus() {
    setOpen(true)
  }

  function updateText(value: string) {
    setText(value)
    setCreateError(null)
    setOpen(true)
  }

  function closeOnEscape() {
    setOpen(false)
  }

  return {
    text,
    open,
    spelling,
    spellingIsFallback,
    results,
    creating,
    createError,
    containerRef,
    term,
    pick,
    addNew,
    focus,
    updateText,
    closeOnEscape,
  }
}
