import { requestContext, withRequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useEffect, useRef, useState } from 'react'
import { createArchiveEvent, createEventCandidateReviewDecision, createLocation, createPerson, createReviewDecision, createSceneObservationReviewDecision, detachEventPhoto, fetchPhotoAnalysis, queueEventAnalysis, queueFaceAnalysis, queueSceneAnalysis, queueSceneObservations, revokeLocationPhoto, revokeReferenceFace } from '../../api/photoAnalysis'
import type { PhotoAnalysis } from '../../api/photoAnalysis'
import { fetchAssets } from '../../api/assets'
import type { AssetSummary } from '../../api/assets'
import { useResourceChanges } from '../../hooks/useResourceChanges'

export function usePhotoAnalysisSection() {
  const [data, setData] = useState<PhotoAnalysis | null>(null)
  const [assets, setAssets] = useState<AssetSummary[]>([])
  const [error, setError] = useState<string | null>(null)
  const latestReload = useRef(0)
  const reload = useCallback(() => withRequestContext(requestContext('PhotoAnalysisSection'), () => {
    const revision = ++latestReload.current
    void Promise.all([fetchPhotoAnalysis(), fetchAssets()]).then(([analysis, allAssets]) => {
      if (revision !== latestReload.current) return
      setData(analysis); setAssets(allAssets); setError(null)
    }).catch((err: unknown) => {
      if (revision === latestReload.current) setError(err instanceof Error ? err.message : 'Unexpected error')
    })
  }), [])
  useEffect(() => withRequestContext({ trigger: 'mount' }, reload), [reload])
  useResourceChanges('photo-analysis', reload)
  useResourceChanges('assets', reload)

  async function save(action: () => Promise<unknown>) { setError(null); try { await action(); reload() } catch (err) { setError(err instanceof Error ? err.message : 'Unexpected error') } }
  return {
    data, assets: assets.filter(asset => asset.contentType.startsWith('image/')), error,
    addPerson: (name: string) => void save(() => createPerson(name)),
    addLocation: (name: string, kind: string) => void save(() => createLocation(name, kind)),
    addEvent: (title: string, occurredOn: string | null, locationId: string | null, personIds: string[], assetIds: string[]) => void save(() => createArchiveEvent({ title, occurredOn, locationId, personIds, assetIds })),
    reviewCandidate: (candidateId: string, kind: string, chosenTargetId: string | null) => void save(() => createReviewDecision(candidateId, { kind, chosenTargetId, note: null })),
    reviewCandidateAsNew: (candidateId: string, createTarget: () => Promise<string>) => void save(async () => {
      const chosenTargetId = await createTarget()
      await createReviewDecision(candidateId, { kind: 'Corrected', chosenTargetId, note: null })
    }),
    reviewSceneObservation: (observationId: string, kind: string) => void save(() => createSceneObservationReviewDecision(observationId, kind)),
    reviewEventCandidate: (candidateId: string, body: { kind: string; chosenEventId: string | null; title: string | null; occurredOn: string | null; locationId: string | null; personIds: string[]; assetIds: string[] }) => void save(() => createEventCandidateReviewDecision(candidateId, body)),
    queueFaceAnalysis: () => void save(queueFaceAnalysis),
    queueSceneAnalysis: () => void save(queueSceneAnalysis),
    queueSceneObservations: () => void save(queueSceneObservations),
    queueEventAnalysis: () => void save(queueEventAnalysis),
    revokeReferenceFace: (personId: string, faceOccurrenceId: string) => void save(() => revokeReferenceFace(personId, faceOccurrenceId)),
    revokeLocationPhoto: (locationId: string, assetId: string) => void save(() => revokeLocationPhoto(locationId, assetId)),
    detachEventPhoto: (eventId: string, assetId: string) => void save(() => detachEventPhoto(eventId, assetId)),
  }
}
