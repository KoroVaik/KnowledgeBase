import { useCallback, useEffect, useState } from 'react'
import { fetchPeopleReview, ignorePeopleReviewRow, submitPeopleReviewRow } from '../../api/photoAnalysis'
import type { PeopleReview, PeopleReviewFace } from '../../api/photoAnalysis'
import { useResourceChanges } from '../../hooks/useResourceChanges'

function errorMessage(err: unknown) {
  return err instanceof Error ? err.message : 'Unexpected error'
}

function faceIds(review: PeopleReview): Set<string> {
  const ids = new Set<string>()
  const add = (faces: PeopleReviewFace[]) => faces.forEach(face => ids.add(face.candidateId))
  review.personRows.forEach(row => add(row.faces))
  review.anonymousRows.forEach(row => add(row.faces))
  review.ignoredGroups.forEach(group => add(group.faces))
  add(review.unsorted)
  return ids
}

export function usePeopleReview() {
  const [review, setReview] = useState<PeopleReview | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [uncheckedIds, setUncheckedIds] = useState<ReadonlySet<string>>(new Set())
  const [busyRows, setBusyRows] = useState<ReadonlySet<string>>(new Set())
  const [names, setNames] = useState<Record<string, string>>({})

  const reload = useCallback(() => {
    void fetchPeopleReview()
      .then(next => {
        setReview(next)
        const present = faceIds(next)
        setUncheckedIds(current => new Set([...current].filter(id => present.has(id))))
      })
      .catch((err: unknown) => setError(errorMessage(err)))
  }, [])
  useEffect(reload, [reload])
  useResourceChanges('photo-analysis', reload)

  function setBusy(rowKey: string, busy: boolean) {
    setBusyRows(current => {
      const next = new Set(current)
      if (busy) next.add(rowKey)
      else next.delete(rowKey)
      return next
    })
  }

  function toggleFace(candidateId: string) {
    setUncheckedIds(current => {
      const next = new Set(current)
      if (next.has(candidateId)) next.delete(candidateId)
      else next.add(candidateId)
      return next
    })
  }

  function split(faces: PeopleReviewFace[]) {
    return {
      candidateIds: faces.filter(face => !uncheckedIds.has(face.candidateId)).map(face => face.candidateId),
      removedCandidateIds: faces.filter(face => uncheckedIds.has(face.candidateId)).map(face => face.candidateId),
    }
  }

  async function runRowAction(rowKey: string, action: () => Promise<void>) {
    setError(null)
    setBusy(rowKey, true)
    try {
      await action()
      setNames(current => ({ ...current, [rowKey]: '' }))
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(rowKey, false)
      reload()
    }
  }

  return {
    review, error,
    isBusy: (rowKey: string) => busyRows.has(rowKey),
    isChecked: (candidateId: string) => !uncheckedIds.has(candidateId),
    checkedCount: (faces: PeopleReviewFace[]) => faces.filter(face => !uncheckedIds.has(face.candidateId)).length,
    toggleFace,
    nameFor: (rowKey: string) => names[rowKey] ?? '',
    setName: (rowKey: string, value: string) => setNames(current => ({ ...current, [rowKey]: value })),
    submitToPerson: (rowKey: string, faces: PeopleReviewFace[], personId: string) =>
      void runRowAction(rowKey, () => submitPeopleReviewRow({ ...split(faces), personId, name: null })),
    submitByName: (rowKey: string, faces: PeopleReviewFace[], name: string) =>
      void runRowAction(rowKey, () => submitPeopleReviewRow({ ...split(faces), personId: null, name: name.trim() })),
    ignore: (rowKey: string, faces: PeopleReviewFace[]) =>
      void runRowAction(rowKey, () => ignorePeopleReviewRow(split(faces))),
    rejectAll: (rowKey: string, faces: PeopleReviewFace[]) =>
      void runRowAction(rowKey, () => ignorePeopleReviewRow({ candidateIds: faces.map(face => face.candidateId), removedCandidateIds: [] })),
  }
}
