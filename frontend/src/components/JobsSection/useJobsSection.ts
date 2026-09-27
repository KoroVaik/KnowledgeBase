import { useDraftProtection } from '../../hooks/useDraftProtection'
import { useSectionRefresh, useVisibleReload } from '../../hooks/useSectionRefresh'
import { requestContext, withRequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useRef, useState } from 'react'
import { fetchJobsSummary, retryAllFailedJobs, retryJob } from '../../api/jobs'
import type { ActiveJob, FailedJob } from '../../api/jobs'

export type JobsState =
  | { status: 'loading' }
  | { status: 'ready'; jobs: ActiveJob[]; failed: FailedJob[] }
  | { status: 'error'; message: string }

const POLL_MS = 5000

/** Loads the active-jobs list and keeps it fresh by polling. The worker is a separate process
 *  with no change stream to the browser (same reason AssetList/NotesList poll after a re-run) -
 *  here it polls while expanded and visible, rather than only after a specific action. */
export function useJobsSection(collapsed: boolean) {
  const refresh = useSectionRefresh<JobsState>({ status: 'loading' }, value => value.status === 'ready', collapsed)
  const { state, receive: setState, canLoad, updateLocal, setError } = refresh
  const latestReload = useRef(0)
  // A job id, 'all', or null when no retry is in flight.
  const [retrying, setRetrying] = useState<string | null>(null)
  const [retryError, setRetryError] = useState<string | null>(null)

  useDraftProtection(retrying !== null, refresh.registerDraft)

  const reload = useCallback((acceptedIds: readonly string[] = []) => withRequestContext(requestContext('JobsSection'), () => {
    if (!canLoad(acceptedIds)) return
    const reloadId = ++latestReload.current

    return fetchJobsSummary()
      .then(({ jobs, failed }) => {
        if (reloadId === latestReload.current) {
          setState({ status: 'ready', jobs, failed }, acceptedIds)
          setError(null)
          updateLocal(current => current.status !== 'ready' ? current : { ...current, jobs: current.jobs.map(job => {
            const latest = jobs.find(next => next.id === job.id)
            return latest ? { ...job, status: latest.status, startedAtUtc: latest.startedAtUtc, attempts: latest.attempts } : job
          }) })
        }
      })
      .catch((error: unknown) => {
        if (reloadId !== latestReload.current) {
          return
        }

        // Fail quietly: stale rows beat blanking them on a flaky connection.
        setError(messageOf(error))
        updateLocal((current) =>
          current.status === 'ready' ? current : { status: 'error', message: messageOf(error) },
        )
      })
  }), [canLoad, setState, updateLocal, setError])

  useVisibleReload(refresh.enabled, () => reload(), POLL_MS)

  const retry = useCallback(
    async (target: string) => {
      setRetrying(target)
      setRetryError(null)

      try {
        await (target === 'all' ? retryAllFailedJobs() : retryJob(target))
        await reload(target === 'all' && state.status === 'ready' ? state.failed.map(job => job.id) : [target])
      } catch (error: unknown) {
        setRetryError(messageOf(error))
      } finally {
        setRetrying(null)
      }
    },
    [reload, state],
  )

  return { state, refresh, reloadSection: () => void refresh.reload(() => reload()), retrying, retryError, retry }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
