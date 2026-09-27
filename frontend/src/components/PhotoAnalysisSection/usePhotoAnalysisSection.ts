import { sameData } from '../../hooks/bufferedUpdates'
import { useDraftProtection } from '../../hooks/useDraftProtection'
import { useSectionRefresh, useVisibleReload } from '../../hooks/useSectionRefresh'
import { requestContext, withRequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useRef, useState } from 'react'
import { createArchiveEvent, createEventCandidateReviewDecision, createLocation, createPerson, createReviewDecision, createSceneObservationReviewDecision, detachEventPhoto, fetchPhotoAnalysis, queueEventAnalysis, queueFaceAnalysis, queueSceneAnalysis, queueSceneObservations, revokeLocationPhoto, revokeReferenceFace } from '../../api/photoAnalysis'
import type { PhotoAnalysis } from '../../api/photoAnalysis'
import { fetchAssets } from '../../api/assets'
import type { AssetSummary } from '../../api/assets'
import { useResourceChanges } from '../../hooks/useResourceChanges'

export function usePhotoAnalysisSection(collapsed: boolean, part: 'people' | 'locations' | 'events') {
  const refresh = useSectionRefresh<{ data: PhotoAnalysis | null; assets: AssetSummary[] }>({ data: null, assets: [] }, value => value.data !== null, collapsed)
  const { state: { data, assets }, receive, updateLocal, canLoad } = refresh
  const [error, setError] = useState<string | null>(null)
  const [busyIds, setBusyIds] = useState<ReadonlySet<string>>(new Set())
  useDraftProtection(busyIds.size > 0, refresh.registerDraft)

  const runningIds = useRef(new Set<string>())
  const latestReload = useRef(0)
  const reload = useCallback((acceptedIds: readonly string[] = []) => withRequestContext(requestContext('PhotoAnalysisSection'), () => {
    if (!canLoad(acceptedIds)) return
    const revision = ++latestReload.current
    return Promise.all([fetchPhotoAnalysis(), fetchAssets()]).then(([analysis, allAssets]) => {
      if (revision !== latestReload.current) return
      const scoped = scopeAnalysis(analysis, part)
      receive({ data: scoped, assets: allAssets }, acceptedIds)
      updateLocal(current => ({ ...current, assets: allAssets, data: current.data === null ? null : {
        ...current.data, faceAnalysisStatus: scoped.faceAnalysisStatus, sceneAnalysisStatus: scoped.sceneAnalysisStatus,
        observationAnalysisStatus: scoped.observationAnalysisStatus, eventAnalysisStatus: scoped.eventAnalysisStatus,
      } }))
      setError(null)
    }).catch((err: unknown) => {
      if (revision === latestReload.current) setError(err instanceof Error ? err.message : 'Unexpected error')
    })
  }), [canLoad, receive, updateLocal, part])
  useVisibleReload(refresh.enabled, () => reload())
  useResourceChanges('photo-analysis', reload)
  useResourceChanges('assets', reload)

  async function save(action: () => Promise<unknown>, rowId?: string) {
    if (rowId && runningIds.current.has(rowId)) return
    if (rowId) { runningIds.current.add(rowId); setBusyIds(new Set(runningIds.current)) }
    setError(null)
    try {
      const createdId = await action()
      await reload([...(rowId ? [rowId] : []), ...(typeof createdId === 'string' ? [createdId] : [])])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unexpected error')
    } finally {
      if (rowId) { runningIds.current.delete(rowId); setBusyIds(new Set(runningIds.current)) }
    }
  }
  const observationAddedToExistingPhoto = data !== null && (refresh.latest.data?.pendingSceneObservations.some(item =>
    !data.pendingSceneObservations.some(old => old.id === item.id) && data.pendingSceneObservations.some(old => old.assetId === item.assetId)) ?? false)
  return {
    data, refresh: { ...refresh, changed: refresh.changed || observationAddedToExistingPhoto || (part === 'events' && data !== null && (!sameData(data.people, refresh.latest.data?.people) || !sameData(data.locations, refresh.latest.data?.locations))) }, reloadSection: () => void refresh.reload(() => reload()), assets: assets.filter(asset => asset.contentType.startsWith('image/')), error,
    isBusy: (rowId: string) => busyIds.has(rowId),
    addPerson: (name: string) => void save(() => createPerson(name)),
    addLocation: (name: string, kind: string) => void save(() => createLocation(name, kind)),
    addEvent: (title: string, occurredOn: string | null, locationId: string | null, personIds: string[], assetIds: string[]) => void save(() => createArchiveEvent({ title, occurredOn, locationId, personIds, assetIds })),
    reviewCandidate: (candidateId: string, kind: string, chosenTargetId: string | null) => void save(() => createReviewDecision(candidateId, { kind, chosenTargetId, note: null }), candidateId),
    reviewCandidateAsNew: (candidateId: string, createTarget: () => Promise<string>) => void save(async () => {
      const chosenTargetId = await createTarget()
      await createReviewDecision(candidateId, { kind: 'Corrected', chosenTargetId, note: null })
    }, candidateId),
    reviewSceneObservation: (observationId: string, kind: string) => void save(() => createSceneObservationReviewDecision(observationId, kind), observationId),
    reviewEventCandidate: (candidateId: string, body: { kind: string; chosenEventId: string | null; title: string | null; occurredOn: string | null; locationId: string | null; personIds: string[]; assetIds: string[] }) => void save(() => createEventCandidateReviewDecision(candidateId, body), candidateId),
    queueFaceAnalysis: () => void save(queueFaceAnalysis),
    queueSceneAnalysis: () => void save(queueSceneAnalysis),
    queueSceneObservations: () => void save(queueSceneObservations),
    queueEventAnalysis: () => void save(queueEventAnalysis),
    revokeReferenceFace: (personId: string, faceOccurrenceId: string) => void save(() => revokeReferenceFace(personId, faceOccurrenceId), personId),
    revokeLocationPhoto: (locationId: string, assetId: string) => void save(() => revokeLocationPhoto(locationId, assetId), locationId),
    detachEventPhoto: (eventId: string, assetId: string) => void save(() => detachEventPhoto(eventId, assetId), eventId),
  }
}

function scopeAnalysis(data: PhotoAnalysis, part: 'people' | 'locations' | 'events'): PhotoAnalysis {
  return {
    ...data,
    people: part === 'locations' ? [] : part === 'events' ? data.people.map(person => ({ ...person, referenceFaces: [], eventCount: 0 })) : data.people,
    locations: part === 'people' ? [] : part === 'events' ? data.locations.map(location => ({ ...location, referencePhotos: [], confirmedPhotoCount: 0, eventCount: 0 })) : data.locations,
    events: part === 'events' ? data.events : [],
    pendingCandidates: part === 'people' ? [] : data.pendingCandidates.filter(candidate => candidate.kind === (part === 'locations' ? 'Location' : 'Event')),
    pendingSceneObservations: part === 'events' ? data.pendingSceneObservations : [],
    pendingEventCandidates: part === 'events' ? data.pendingEventCandidates : [],
    faceAnalysisStatus: part === 'people' ? data.faceAnalysisStatus : { imagesWithoutFingerprint: 0, pendingFingerprintJobs: 0, pendingFaceJobs: 0 },
    sceneAnalysisStatus: part === 'locations' ? data.sceneAnalysisStatus : { pendingSceneJobs: 0 },
    observationAnalysisStatus: part === 'events' ? data.observationAnalysisStatus : { pendingObservationJobs: 0 },
    eventAnalysisStatus: part === 'events' ? data.eventAnalysisStatus : { pendingEventJobs: 0 },
  }
}
