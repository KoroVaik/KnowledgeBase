import { apiFetch, readErrorMessage } from './http'

export interface Person { id: string; name: string; eventCount: number }
export interface Location { id: string; name: string; kind: string; eventCount: number; confirmedPhotoCount: number }
export interface ArchiveEvent { id: string; title: string; occurredOn: string | null; locationId: string | null; locationName: string | null; personIds: string[]; assetIds: string[] }
export interface FaceBounds { x: number; y: number; width: number; height: number }
export interface PhotoAnalysisCandidate { id: string; kind: string; subjectAssetId: string; subjectAssetName: string; subjectFaceOccurrenceId: string | null; proposedTargetId: string | null; proposedTargetName: string | null; proposedLabel: string | null; rank: number; score: number; signalsJson: string; runId: string; faceBounds?: FaceBounds | null }
export interface SceneObservation { id: string; assetId: string; assetName: string; kind: string; subjectPersonName: string | null; relatedPersonName: string | null; description: string; evidence: string; confidence: number; runId: string }
export interface EventCandidatePhoto { id: string; name: string }
export interface EventCandidate { id: string; score: number; suggestedOccurredOn: string | null; signalsJson: string; photos: EventCandidatePhoto[] }
export interface FaceAnalysisStatus { imagesWithoutFingerprint: number; pendingFingerprintJobs: number; pendingFaceJobs: number }
export interface SceneAnalysisStatus { pendingSceneJobs: number }
export interface ObservationAnalysisStatus { pendingObservationJobs: number }
export interface EventAnalysisStatus { pendingEventJobs: number }
export interface PhotoAnalysis { people: Person[]; locations: Location[]; events: ArchiveEvent[]; pendingCandidates: PhotoAnalysisCandidate[]; pendingSceneObservations: SceneObservation[]; pendingEventCandidates: EventCandidate[]; faceAnalysisStatus: FaceAnalysisStatus; sceneAnalysisStatus: SceneAnalysisStatus; observationAnalysisStatus: ObservationAnalysisStatus; eventAnalysisStatus: EventAnalysisStatus }
interface PhotoAnalysisWire extends Omit<PhotoAnalysis, 'faceAnalysisStatus' | 'sceneAnalysisStatus' | 'observationAnalysisStatus' | 'eventAnalysisStatus' | 'pendingSceneObservations' | 'pendingEventCandidates'> { faceAnalysisStatus?: FaceAnalysisStatus; sceneAnalysisStatus?: SceneAnalysisStatus; observationAnalysisStatus?: ObservationAnalysisStatus; eventAnalysisStatus?: EventAnalysisStatus; pendingSceneObservations?: SceneObservation[]; pendingEventCandidates?: EventCandidate[] }

export async function fetchPhotoAnalysis(): Promise<PhotoAnalysis> {
  const response = await apiFetch('/api/photo-analysis')
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Could not load photo analysis'))
  const analysis = await response.json() as PhotoAnalysisWire
  return {
    ...analysis,
    pendingSceneObservations: analysis.pendingSceneObservations ?? [],
    pendingEventCandidates: analysis.pendingEventCandidates ?? [],
    faceAnalysisStatus: analysis.faceAnalysisStatus ?? { imagesWithoutFingerprint: 0, pendingFingerprintJobs: 0, pendingFaceJobs: 0 },
    sceneAnalysisStatus: analysis.sceneAnalysisStatus ?? { pendingSceneJobs: 0 },
    observationAnalysisStatus: analysis.observationAnalysisStatus ?? { pendingObservationJobs: 0 },
    eventAnalysisStatus: analysis.eventAnalysisStatus ?? { pendingEventJobs: 0 },
  }
}

async function create(path: string, body: unknown): Promise<void> {
  const response = await apiFetch(`/api/photo-analysis/${path}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) })
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Could not save photo analysis data'))
}

export const createPerson = (name: string) => create('people', { name })
export const createLocation = (name: string, kind: string) => create('locations', { name, kind })
export const createArchiveEvent = (body: { title: string; occurredOn: string | null; locationId: string | null; personIds: string[]; assetIds: string[] }) => create('events', body)
export const createReviewDecision = (candidateId: string, body: { kind: string; chosenTargetId: string | null; note: string | null }) => create(`candidates/${candidateId}/decisions`, body)
export const createSceneObservationReviewDecision = (observationId: string, kind: string) => create(`observations/${observationId}/decisions`, { kind, note: null })
export const createEventCandidateReviewDecision = (candidateId: string, body: { kind: string; chosenEventId: string | null; title: string | null; occurredOn: string | null; locationId: string | null; personIds: string[]; assetIds: string[] }) => create(`event-candidates/${candidateId}/decisions`, { ...body, note: null })
export const queueFaceAnalysis = () => create('analyze-faces', {})
export const queueSceneAnalysis = () => create('analyze-scenes', {})
export const queueSceneObservations = () => create('analyze-observations', {})
export const queueEventAnalysis = () => create('analyze-events', {})
