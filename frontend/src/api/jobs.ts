import { apiFetch, readErrorMessage } from './http'

/** Mirrors ActiveJobResponse in backend Controllers/Jobs. */
export interface ActiveJob {
  id: string
  kind: string
  kindDescription: string
  status: 'Pending' | 'Running'
  /** Set only for a BuildSourceNote job - the other kinds carry no asset. */
  assetId: string | null
  assetFileName: string | null
  createdAtUtc: string
  startedAtUtc: string | null
  attempts: number
  /** The previous attempt's failure message, once the worker has retried this job and handed
   *  it back to the queue. Null on a job that has not failed yet. */
  error: string | null
}

/** Jobs the pipeline has queued (Pending) or is running right now, oldest first. */
export async function fetchActiveJobs(): Promise<ActiveJob[]> {
  const response = await apiFetch('/api/jobs')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the jobs'))
  }

  return (await response.json()) as ActiveJob[]
}

/** When a Done/Skipped job of any of these kinds last finished, or null if none has. */
export async function fetchLastCompleted(kinds: string[]): Promise<string | null> {
  const query = new URLSearchParams(kinds.map((kind) => ['kind', kind]))
  const response = await apiFetch(`/api/jobs/last-completed?${query}`)

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the last run'))
  }

  const body = (await response.json()) as { completedAtUtc: string | null }
  return body.completedAtUtc
}

/** Mirrors FailedJobResponse in backend Controllers/Jobs. */
export interface FailedJob {
  id: string
  kind: string
  kindDescription: string
  assetId: string | null
  assetFileName: string | null
  createdAtUtc: string
  completedAtUtc: string | null
  attempts: number
  error: string | null
}

/** Jobs the worker gave up on, most recently failed first. */
export async function fetchFailedJobs(): Promise<FailedJob[]> {
  const response = await apiFetch('/api/jobs/failed')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the failed jobs'))
  }

  return (await response.json()) as FailedJob[]
}

export async function retryJob(id: string): Promise<void> {
  const response = await apiFetch(`/api/jobs/${encodeURIComponent(id)}/retry`, { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not retry the job'))
  }
}

export async function retryAllFailedJobs(): Promise<void> {
  const response = await apiFetch('/api/jobs/failed/retry', { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not retry the failed jobs'))
  }
}
