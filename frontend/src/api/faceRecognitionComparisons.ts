import { apiFetch, readErrorMessage } from './http'
import { requestContext } from '../diagnostics/diagnostics'
import type { RequestContext } from '../diagnostics/diagnostics'
import type { FaceBounds } from './photoAnalysis'

export interface RecognitionComparisonResult {
  id: string
  modelId: string
  modelName: string
  error: string | null
  elapsedMilliseconds: number | null
  completedAtUtc: string | null
  threshold: number | null
  samePersonPairs: number
  differentPersonPairs: number
  truePositives: number
  falsePositives: number
  trueNegatives: number
  falseNegatives: number
  configuration: Record<string, unknown>
}
export interface RecognitionEvidenceFace { id: string; assetId: string; bounds: FaceBounds }
export interface RecognitionEvidence {
  id: string
  isSamePerson: boolean
  first: RecognitionEvidenceFace
  second: RecognitionEvidenceFace
  scores: { resultId: string; score: number; isMatch: boolean }[]
}
export interface RecognitionComparisonRun {
  id: string
  createdAtUtc: string
  photoCount: number
  referenceFaceCount: number
  personCount: number
  status: string
  error: string | null
  results: RecognitionComparisonResult[]
  evidence: RecognitionEvidence[]
}
export interface RecognitionComparisonHistory {
  models: { id: string; name: string }[]
  pendingPhotoCount: number
  runs: { id: string; createdAtUtc: string; photoCount: number; referenceFaceCount: number; personCount: number; status: string }[]
  hasMore: boolean
}

const base = '/api/photo-analysis/face-recognition-comparisons'
async function request<T>(path: string, method = 'GET', context: RequestContext = requestContext('FaceRecognitionComparison')): Promise<T> {
  const response = await apiFetch(`${base}${path}`, { method }, context)
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Could not update face recognition comparison'))
  return await response.json() as T
}

export const fetchRecognitionComparisonHistory = (page: number, context?: RequestContext) => request<RecognitionComparisonHistory>(`?page=${page}`, 'GET', context)
export const fetchRecognitionComparison = (id: string, context?: RequestContext) => request<RecognitionComparisonRun>(`/${encodeURIComponent(id)}`, 'GET', context)
export const createRecognitionComparison = () => request<{ id: string | null }>('', 'POST')
