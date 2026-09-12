import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchNotes } from '../../api/notes'
import { notesText } from '../../format'
import {
  acceptTagParentSuggestion,
  addTagParent as addTagParentApi,
  confirmTag as confirmTagApi,
  deleteTag,
  fetchTags,
  mergeTag,
  rejectTagParentSuggestion,
  removeTagParent as removeTagParentApi,
  suggestTagHierarchy,
  suggestTagMerges,
  synthesiseIndex,
  synthesiseTag as synthesiseTagApi,
} from '../../api/tags'
import type { Tag } from '../../api/tags'
import { useResourceChanges } from '../../hooks/useResourceChanges'

export type TagsState =
  | { status: 'loading' }
  | { status: 'ready'; tags: Tag[]; synthesisCount: number }
  | { status: 'error'; message: string }

/** State and every action behind TagsSection: load/reload, confirm, merge, delete, the review
 *  suggestion jobs (suggest-review, build index), and parent add/remove. `TagsSection.tsx` only
 *  turns this into markup - `queued`/`busy` key off the same string each action uses (a tag id,
 *  or 'suggest-review' / 'suggest-merges' / 'suggest-hierarchy' / 'index') so a button can tell
 *  whether it is the one running. */
export function useTagsSection() {
  const [state, setState] = useState<TagsState>({ status: 'loading' })
  const [busy, setBusy] = useState<string | null>(null)
  const [queued, setQueued] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  const latestReload = useRef(0)
  const noticeTimer = useRef<number | undefined>(undefined)

  // Read at the top of reload(), before the fetch races ahead - tells us what a queued job
  // (suggest-merges/-hierarchy, build index, per-tag synthesis) finished into.
  const stateRef = useRef(state)
  const queuedRef = useRef(queued)
  useEffect(() => {
    stateRef.current = state
  }, [state])
  useEffect(() => {
    queuedRef.current = queued
  }, [queued])

  const reload = useCallback(() => {
    const reloadId = ++latestReload.current
    const previous = stateRef.current
    const finishedLabels = queuedRef.current

    void Promise.all([fetchTags(), fetchNotes('Synthesis')])
      .then(([tags, syntheses]) => {
        if (reloadId !== latestReload.current) {
          return
        }

        if (finishedLabels.length > 0 && previous.status === 'ready') {
          window.clearTimeout(noticeTimer.current)
          setNotice(describeOutcome(finishedLabels, previous, tags))
          noticeTimer.current = window.setTimeout(() => setNotice(null), 6000)
        }

        setState({ status: 'ready', tags, synthesisCount: syntheses.length })
        setQueued([])
      })
      .catch((err: unknown) => {
        if (reloadId !== latestReload.current) {
          return
        }

        setState((current) =>
          current.status === 'ready' ? current : { status: 'error', message: messageOf(err) },
        )
      })
  }, [])

  useEffect(reload, [reload])
  useResourceChanges('notes', reload)
  useEffect(() => () => window.clearTimeout(noticeTimer.current), [])

  async function run(key: string, action: () => Promise<void>): Promise<boolean> {
    setError(null)
    setBusy(key)

    try {
      await action()
      return true
    } catch (err) {
      setError(messageOf(err))
      return false
    } finally {
      setBusy(null)
    }
  }

  async function synthesise(label: string, action: () => Promise<void>) {
    if (await run(label, action)) {
      setQueued((current) => [...current, label])
    }
  }

  async function change(key: string, action: () => Promise<void>) {
    if (await run(key, action)) {
      reload()
    }
  }

  function remove(tag: Tag) {
    if (
      tag.confirmed &&
      !window.confirm(
        `Delete “${tag.name}”? It comes off ${notesText(tag.noteCount)}, and its synthesis note goes to the bin.`,
      )
    ) {
      return
    }

    void change(tag.id, () => deleteTag(tag.id))
  }

  function merge(tag: Tag, into: Tag) {
    void change(tag.id, () => mergeTag(tag.id, into.id))
  }

  function confirm(tag: Tag) {
    void change(tag.id, () => confirmTagApi(tag.id))
  }

  // Confirms the tag and accepts one of its AI parent suggestions in one action - the merged
  // "90% AI, one approval" flow instead of confirming now and finding the same tag again later
  // in "To place".
  function confirmWithParent(tag: Tag, parent: Tag) {
    void change(tag.id, async () => {
      await confirmTagApi(tag.id)
      await acceptTagParentSuggestion(tag.id, parent.id)
    })
  }

  // The Flip control's commit for an unconfirmed tag's own suggested parent: same outcome as
  // confirmWithParent, but the model had parent/child backwards. acceptTagParentSuggestion always
  // honours the direction the suggestion row was stored in regardless of argument order, so a
  // reversed link needs the plain addParent + a reject to clear the original guess.
  function confirmWithParentFlipped(tag: Tag, candidate: Tag) {
    void change(tag.id, async () => {
      await confirmTagApi(tag.id)
      await addTagParentApi(candidate.id, tag.id)
      await rejectTagParentSuggestion(tag.id, candidate.id)
    })
  }

  // Same idea for a confirmed tag's placement suggestion (TagPlacementSuggestions) - no confirm
  // needed there, just the reversed link plus dismissing the original guess.
  function flipSuggestion(newChildId: string, newParentId: string) {
    void change(`${newChildId}:${newParentId}`, async () => {
      await addTagParentApi(newChildId, newParentId)
      await rejectTagParentSuggestion(newChildId, newParentId)
    })
  }

  function synthesiseTagNote(tag: Tag) {
    void synthesise(tag.name, () => synthesiseTagApi(tag.name))
  }

  // One button queues both passes: the merge re-check needs an unreviewed tag, the placement
  // search just needs two tags to compare - each is skipped when its own precondition is not
  // met, rather than surfacing a 409 for a check the UI could see coming.
  function suggestForReview() {
    void runSuggestForReview()
  }

  async function runSuggestForReview() {
    if (state.status !== 'ready') {
      return
    }

    const canSuggestMerges = state.tags.length >= 2 && state.tags.some((tag) => !tag.confirmed)
    const canSuggestHierarchy = state.tags.length >= 2

    if (!canSuggestMerges && !canSuggestHierarchy) {
      return
    }

    setError(null)
    setBusy('suggest-review')

    const queuedLabels: string[] = []
    const errors: string[] = []

    if (canSuggestMerges) {
      try {
        await suggestTagMerges()
        queuedLabels.push('suggest-merges')
      } catch (err) {
        errors.push(messageOf(err))
      }
    }

    if (canSuggestHierarchy) {
      try {
        await suggestTagHierarchy()
        queuedLabels.push('suggest-hierarchy')
      } catch (err) {
        errors.push(messageOf(err))
      }
    }

    setBusy(null)

    if (queuedLabels.length > 0) {
      setQueued((current) => [...current, ...queuedLabels])
    }

    if (queuedLabels.length === 0 && errors.length > 0) {
      setError(errors.join(' · '))
    }
  }

  function buildIndex() {
    void synthesise('index', synthesiseIndex)
  }

  function addParent(childId: string, parentId: string) {
    void change(childId, () => addTagParentApi(childId, parentId))
  }

  function removeParent(childId: string, parentId: string) {
    void change(childId, () => removeTagParentApi(childId, parentId))
  }

  function acceptSuggestion(childId: string, parentId: string) {
    void change(`${childId}:${parentId}`, () => acceptTagParentSuggestion(childId, parentId))
  }

  function rejectSuggestion(childId: string, parentId: string) {
    void change(`${childId}:${parentId}`, () => rejectTagParentSuggestion(childId, parentId))
  }

  return {
    state,
    busy,
    queued,
    error,
    notice,
    remove,
    merge,
    confirm,
    confirmWithParent,
    confirmWithParentFlipped,
    flipSuggestion,
    synthesiseTagNote,
    suggestForReview,
    buildIndex,
    addParent,
    removeParent,
    acceptSuggestion,
    rejectSuggestion,
  }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}

// Only merges/hierarchy leave a countable trace (they write no note, just tag rows) - the
// others (index, per-tag synthesis) just get a generic "finished", since success and a
// Skipped/Failed job both only ever show up here as "the queue cleared".
function describeOutcome(
  labels: string[],
  previous: Extract<TagsState, { status: 'ready' }>,
  tags: Tag[],
): string {
  return labels
    .map((label) => {
      if (label === 'suggest-merges') {
        return countMessage(
          'Suggest merges',
          countMergeSuggestions(previous.tags),
          countMergeSuggestions(tags),
          'merge suggestion',
        )
      }

      if (label === 'suggest-hierarchy') {
        return countMessage(
          'Suggest hierarchy',
          previous.tags.filter((tag) => tag.hasPendingPlacementSuggestion).length,
          tags.filter((tag) => tag.hasPendingPlacementSuggestion).length,
          'placement suggestion',
        )
      }

      if (label === 'index') {
        return 'Build index: finished'
      }

      return `Synthesise “${label}”: finished`
    })
    .join(' · ')
}

function countMergeSuggestions(tags: Tag[]): number {
  return tags.filter((tag) => !tag.confirmed && tag.suggestedMergeIntoId !== null).length
}

function countMessage(label: string, before: number, after: number, noun: string): string {
  const gained = after - before

  if (gained <= 0) {
    return `${label}: nothing new`
  }

  return `${label}: ${gained} new ${noun}${gained === 1 ? '' : 's'}`
}
