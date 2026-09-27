import { useDraftProtection } from '../../hooks/useDraftProtection'
import { useSectionRefresh, useVisibleReload } from '../../hooks/useSectionRefresh'
import { requestContext, withRequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useRef, useState } from 'react'
import { fetchPeopleReview, ignorePeopleReviewRow, submitPeopleReviewRow, reviewFaceValidation, retryFaceValidation } from '../../api/photoAnalysis'
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

export function usePeopleReview(collapsed: boolean) {
  const refresh = useSectionRefresh<PeopleReview | null>(null, value => value !== null, collapsed)
  const { state: review, receive: setReview, canLoad } = refresh
  const [error, setError] = useState<string | null>(null)
  const [uncheckedIds, setUncheckedIds] = useState<ReadonlySet<string>>(new Set())
  const [busyRows, setBusyRows] = useState<ReadonlySet<string>>(new Set())
  const [names, setNames] = useState<Record<string, string>>({})

  useDraftProtection(uncheckedIds.size > 0 || busyRows.size > 0 || Object.values(names).some(name => name.trim() !== ''), refresh.registerDraft)

  const latestReload = useRef(0)
  const reload = useCallback((acceptedIds: readonly string[] = []) => withRequestContext(requestContext('PeopleReviewSection'), () => {
    if (!canLoad(acceptedIds)) return
    const revision = ++latestReload.current
    return fetchPeopleReview()
      .then(next => {
        if (revision !== latestReload.current) return
        setError(null)
        setReview(next, acceptedIds)

      })
      .catch((err: unknown) => { if (revision === latestReload.current) setError(errorMessage(err)) })
  }), [canLoad, setReview])
  useVisibleReload(refresh.enabled, () => reload())
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

  async function runRowAction(rowKey: string, action: () => Promise<void>, completedFaces?: PeopleReviewFace[]) {
    setError(null)
    setBusy(rowKey, true)
    try {
      await action()
      if (completedFaces) {
        setNames(current => ({ ...current, [rowKey]: '' }))
        const completed = new Set(completedFaces.map(face => face.candidateId))
        setUncheckedIds(current => new Set([...current].filter(id => !completed.has(id))))
      }
      await reload([rowKey.slice(rowKey.indexOf(':') + 1)])
    } catch (err) {
      setError(errorMessage(err))
    } finally {
      setBusy(rowKey, false)
    }
  }

  return {
    review, error, refresh, reloadSection: () => void refresh.reload(() => reload()),
    latest: refresh.latest,
    newRows: <T extends { faces: PeopleReviewFace[] }>(rows: T[]) => {
      const existingFaces = review === null ? new Set<string>() : faceIds(review)
      return rows.filter(row => !row.faces.some(face => existingFaces.has(face.candidateId)))
    },
    isBusy: (rowKey: string) => busyRows.has(rowKey),
    isChecked: (candidateId: string) => !uncheckedIds.has(candidateId),
    checkedCount: (faces: PeopleReviewFace[]) => faces.filter(face => !uncheckedIds.has(face.candidateId)).length,
    toggleFace,
    nameFor: (rowKey: string) => names[rowKey] ?? '',
    setName: (rowKey: string, value: string) => setNames(current => ({ ...current, [rowKey]: value })),
    submitToPerson: (rowKey: string, faces: PeopleReviewFace[], personId: string) =>
      void runRowAction(rowKey, () => submitPeopleReviewRow({ ...split(faces), personId, name: null }), faces),
    submitByName: (rowKey: string, faces: PeopleReviewFace[], name: string) =>
      void runRowAction(rowKey, () => submitPeopleReviewRow({ ...split(faces), personId: null, name: name.trim() }), faces),
    ignore: (rowKey: string, faces: PeopleReviewFace[]) =>
      void runRowAction(rowKey, () => ignorePeopleReviewRow(split(faces)), faces),
    rejectAll: (rowKey: string, faces: PeopleReviewFace[]) =>
      void runRowAction(rowKey, () => ignorePeopleReviewRow({ candidateIds: faces.map(face => face.candidateId), removedCandidateIds: [] }), faces),
    validate: (rowKey: string, face: PeopleReviewFace, kind: 'Approved' | 'Excluded') =>
      void runRowAction(rowKey, () => reviewFaceValidation(face.faceOccurrenceId, kind)),
    retryValidation: (rowKey: string, face: PeopleReviewFace) =>
      void runRowAction(rowKey, () => retryFaceValidation(face.faceOccurrenceId)),
  }
}
