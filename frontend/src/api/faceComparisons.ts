import { apiFetch, readErrorMessage } from './http'
import { requestContext } from '../diagnostics/diagnostics'
import type { RequestContext } from '../diagnostics/diagnostics'
import type { FaceBounds } from './photoAnalysis'

export interface ComparisonDetection {
  id: string
  ordinal: number
  bounds: FaceBounds
  score: number
  landmarks: { x: number; y: number }[]
  warnings: string[]
  isFace: boolean | null
}
export interface ComparisonResult {
  id: string
  modelId: string
  modelName: string
  error: string | null
  elapsedMilliseconds: number | null
  completedAtUtc: string | null
  missedFaces: number | null
  configuration: Record<string, unknown>
  detections: ComparisonDetection[]
}
export interface ComparisonSummary {
  id: string
  assetId: string
  assetName: string
  createdAtUtc: string
  isSkipped: boolean
  reviewedAtUtc: string | null
  status: string
}
export interface ComparisonRun {
  id: string
  assetId: string
  createdAtUtc: string
  imageWidth: number | null
  imageHeight: number | null
  contentSha256: string | null
  isSkipped: boolean
  reviewedAtUtc: string | null
  status: string
  error: string | null
  results: ComparisonResult[]
}
export interface ComparisonHistory {
  models: { id: string; name: string }[]
  runs: ComparisonSummary[]
  hasMore: boolean
}

const base = '/api/photo-analysis/face-comparisons'
async function request<T>(path: string, method = 'GET', body?: unknown, context: RequestContext = requestContext('FaceComparison')): Promise<T> {
  const response = await apiFetch(`${base}${path}`, { method,
    ...(body === undefined ? {} : { headers: { 'content-type': 'application/json' }, body: JSON.stringify(body) }) }, context)
  if (!response.ok) throw new Error(await readErrorMessage(response, 'Could not update face comparison'))
  return response.status === 204 ? undefined as T : await response.json() as T
}

export const fetchComparisonHistory = (assetId: string, page: number, showReviewed: boolean, context?: RequestContext) =>
  request<ComparisonHistory>(`?page=${page}&showReviewed=${showReviewed}${assetId ? `&assetId=${encodeURIComponent(assetId)}` : ''}`, 'GET', undefined, context)
export const fetchComparison = (id: string, context?: RequestContext) => request<ComparisonRun>(`/${encodeURIComponent(id)}`, 'GET', undefined, context)
export const createComparison = (assetId: string) => request<{ id: string }>('', 'POST', { assetId })
export const reviewDetection = (id: string, isFace: boolean | null) => request<void>(`/detections/${encodeURIComponent(id)}/review`, 'PUT', { isFace })
export const setMissedFaces = (id: string, count: number | null) => request<void>(`/results/${encodeURIComponent(id)}/missed-faces`, 'PUT', { count })
export const reviewComparisonDetections = (runId: string, reviews: { id: string; isFace: boolean }[]) =>
  request<void>(`/${encodeURIComponent(runId)}/detections/review`, 'PUT', { reviews })
export const setComparisonMissedFaces = (runId: string, count: number | null) =>
  request<void>(`/${encodeURIComponent(runId)}/missed-faces`, 'PUT', { count })
export const setComparisonSkipped = (runId: string, isSkipped: boolean) =>
  request<void>(`/${encodeURIComponent(runId)}/skipped`, 'PUT', { isSkipped })
export const setComparisonReviewed = (runId: string, isReviewed: boolean) =>
  request<void>(`/${encodeURIComponent(runId)}/reviewed`, 'PUT', { isReviewed })
