import { apiFetch, readErrorMessage } from './http'

/** Mirrors ActiveJobResponse in backend Controllers/Jobs. */
export interface ActiveJob {
  id: string
  kind: string
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
