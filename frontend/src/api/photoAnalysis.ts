import { apiFetch, readErrorMessage } from './http'

export interface FaceBounds { x: number; y: number; width: number; height: number }
export interface PersonReferenceFace { id: string; assetId: string; assetName: string; faceBounds: FaceBounds; confirmedAtUtc: string }
export interface Person { id: string; name: string; eventCount: number; referenceFaces: PersonReferenceFace[] }
export interface LocationReferencePhoto { assetId: string; assetName: string; confirmedAtUtc: string }
export interface Location { id: string; name: string; kind: string; eventCount: number; confirmedPhotoCount: number; referencePhotos: LocationReferencePhoto[] }
export interface ArchiveEvent { id: string; title: string; occurredOn: string | null; locationId: string | null; locationName: string | null; personIds: string[]; assetIds: string[] }
// score is the raw stored measure; confidence is the calibrated 0-1 form the UI shows as a percent.
export interface PhotoAnalysisCandidateMatch { targetId: string; score: number; confidence: number }
export interface PhotoAnalysisCandidate { id: string; kind: string; subjectAssetId: string; subjectAssetName: string; subjectFaceOccurrenceId: string | null; proposedTargetId: string | null; proposedTargetName: string | null; proposedLabel: string | null; rank: number; score: number; confidence: number; signalsJson: string; runId: string; faceBounds?: FaceBounds | null; matches: PhotoAnalysisCandidateMatch[] }
export interface SceneObservation { id: string; assetId: string; assetName: string; kind: string; subjectPersonName: string | null; relatedPersonName: string | null; description: string; evidence: string; confidence: number; runId: string }
export interface EventCandidatePhoto { id: string; name: string }
export interface EventCandidate { id: string; score: number; suggestedOccurredOn: string | null; signalsJson: string; photos: EventCandidatePhoto[] }
export interface FaceAnalysisStatus { imagesWithoutFingerprint: number; pendingFingerprintJobs: number; pendingFaceJobs: number }
export interface SceneAnalysisStatus { pendingSceneJobs: number }
export interface ObservationAnalysisStatus { pendingObservationJobs: number }
export interface EventAnalysisStatus { pendingEventJobs: number }
export interface PhotoAnalysis { people: Person[]; locations: Location[]; events: ArchiveEvent[]; pendingCandidates: PhotoAnalysisCandidate[]; pendingSceneObservations: SceneObservation[]; pendingEventCandidates: EventCandidate[]; faceAnalysisStatus: FaceAnalysisStatus; sceneAnalysisStatus: SceneAnalysisStatus; observationAnalysisStatus: ObservationAnalysisStatus; eventAnalysisStatus: EventAnalysisStatus }
interface PersonWire extends Omit<Person, 'referenceFaces'> { referenceFaces?: PersonReferenceFace[] }
interface LocationWire extends Omit<Location, 'referencePhotos'> { referencePhotos?: LocationReferencePhoto[] }
interface CandidateWire extends Omit<PhotoAnalysisCandidate, 'matches'> { matches?: PhotoAnalysisCandidateMatch[] }
interface PhotoAnalysisWire extends Omit<PhotoAnalysis, 'faceAnalysisStatus' | 'sceneAnalysisStatus' | 'observationAnalysisStatus' | 'eventAnalysisStatus' | 'pendingSceneObservations' | 'pendingEventCandidates' | 'people' | 'locations' | 'pendingCandidates'> { people: PersonWire[]; locations: LocationWire[]; pendingCandidates: CandidateWire[]; faceAnalysisStatus?: FaceAnalysisStatus; sceneAnalysisStatus?: SceneAnalysisStatus; observationAnalysisStatus?: ObservationAnalysisStatus; eventAnalysisStatus?: EventAnalysisStatus; pendingSceneObservations?: SceneObservation[]; pendingEventCandidates?: EventCandidate[] }

export async function fetchPhotoAnalysis(): Promise<PhotoAnalysis> {
  const response = await apiFetch('/api/photo-analysis')
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Could not load photo analysis'))
  const analysis = await response.json() as PhotoAnalysisWire
  return {
    ...analysis,
    people: analysis.people.map(person => ({ ...person, referenceFaces: person.referenceFaces ?? [] })),
    locations: analysis.locations.map(location => ({ ...location, referencePhotos: location.referencePhotos ?? [] })),
    pendingCandidates: analysis.pendingCandidates.map(candidate => ({ ...candidate, matches: candidate.matches ?? [] })),
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

async function createWithId(path: string, body: unknown): Promise<string> {
  const response = await apiFetch(`/api/photo-analysis/${path}`, { method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) })
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Could not save photo analysis data'))
  return (await response.json() as { id: string }).id
}

export const createPerson = (name: string) => createWithId('people', { name })
export const createLocation = (name: string, kind: string) => createWithId('locations', { name, kind })
export const createArchiveEvent = (body: { title: string; occurredOn: string | null; locationId: string | null; personIds: string[]; assetIds: string[] }) => createWithId('events', body)
export const createReviewDecision = (candidateId: string, body: { kind: string; chosenTargetId: string | null; note: string | null }) => create(`candidates/${candidateId}/decisions`, body)
export const createSceneObservationReviewDecision = (observationId: string, kind: string) => create(`observations/${observationId}/decisions`, { kind, note: null })
export const createEventCandidateReviewDecision = (candidateId: string, body: { kind: string; chosenEventId: string | null; title: string | null; occurredOn: string | null; locationId: string | null; personIds: string[]; assetIds: string[] }) => create(`event-candidates/${candidateId}/decisions`, { ...body, note: null })
export const queueFaceAnalysis = () => create('analyze-faces', {})
export const queueSceneAnalysis = () => create('analyze-scenes', {})
export const queueSceneObservations = () => create('analyze-observations', {})
export const queueEventAnalysis = () => create('analyze-events', {})

async function remove(path: string, failure: string): Promise<void> {
  const response = await apiFetch(`/api/photo-analysis/${path}`, { method: 'DELETE' })
  if (!response.ok) throw new Error(await readErrorMessage(response, failure))
}

export const revokeReferenceFace = (personId: string, faceOccurrenceId: string) =>
  remove(`people/${personId}/reference-faces/${faceOccurrenceId}`, 'Could not revoke this face')
export const revokeLocationPhoto = (locationId: string, assetId: string) =>
  remove(`locations/${locationId}/reference-photos/${assetId}`, 'Could not revoke this location photo')
export const detachEventPhoto = (eventId: string, assetId: string) =>
  remove(`events/${eventId}/photos/${assetId}`, 'Could not remove this photo from the event')
