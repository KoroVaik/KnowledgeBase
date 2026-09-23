import { requestContext, withRequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchJobsSummary, retryAllFailedJobs, retryJob } from '../../api/jobs'
import type { ActiveJob, FailedJob } from '../../api/jobs'

export type JobsState =
  | { status: 'loading' }
  | { status: 'ready'; jobs: ActiveJob[]; failed: FailedJob[] }
  | { status: 'error'; message: string }

const POLL_MS = 5000

/** Loads the active-jobs list and keeps it fresh by polling. The worker is a separate process
 *  with no change stream to the browser (same reason AssetList/NotesList poll after a re-run) -
 *  here that is the whole point of the section, so it polls continuously rather than only after
 *  a specific action. */
export function useJobsSection() {
  const [state, setState] = useState<JobsState>({ status: 'loading' })
  const latestReload = useRef(0)
  // A job id, 'all', or null when no retry is in flight.
  const [retrying, setRetrying] = useState<string | null>(null)
  const [retryError, setRetryError] = useState<string | null>(null)

  const reload = useCallback(() => withRequestContext(requestContext('JobsSection'), () => {
    const reloadId = ++latestReload.current

    return fetchJobsSummary()
      .then(({ jobs, failed }) => {
        if (reloadId === latestReload.current) {
          setState({ status: 'ready', jobs, failed })
        }
      })
      .catch((error: unknown) => {
        if (reloadId !== latestReload.current) {
          return
        }

        // Fail quietly: stale rows beat blanking them on a flaky connection.
        setState((current) =>
          current.status === 'ready' ? current : { status: 'error', message: messageOf(error) },
        )
      })
  }), [])

  useEffect(() => {
    let stopped = false
    let timer: ReturnType<typeof setTimeout> | undefined
    const poll = async (trigger: string) => {
      await withRequestContext({ trigger }, reload)
      if (!stopped) timer = setTimeout(() => void poll('poll'), POLL_MS)
    }
    void poll('mount')
    return () => { stopped = true; clearTimeout(timer) }
  }, [reload])

  const retry = useCallback(
    async (target: string) => {
      setRetrying(target)
      setRetryError(null)

      try {
        await (target === 'all' ? retryAllFailedJobs() : retryJob(target))
        reload()
      } catch (error: unknown) {
        setRetryError(messageOf(error))
      } finally {
        setRetrying(null)
      }
    },
    [reload],
  )

  return { state, retrying, retryError, retry }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
