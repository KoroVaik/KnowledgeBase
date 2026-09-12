import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchActiveJobs } from '../../api/jobs'
import type { ActiveJob } from '../../api/jobs'

export type JobsState =
  | { status: 'loading' }
  | { status: 'ready'; jobs: ActiveJob[] }
  | { status: 'error'; message: string }

const POLL_MS = 5000

/** Loads the active-jobs list and keeps it fresh by polling. The worker is a separate process
 *  with no change stream to the browser (same reason AssetList/NotesList poll after a re-run) -
 *  here that is the whole point of the section, so it polls continuously rather than only after
 *  a specific action. */
export function useJobsSection() {
  const [state, setState] = useState<JobsState>({ status: 'loading' })
  const latestReload = useRef(0)

  const reload = useCallback(() => {
    const reloadId = ++latestReload.current

    fetchActiveJobs()
      .then((jobs) => {
        if (reloadId === latestReload.current) {
          setState({ status: 'ready', jobs })
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
  }, [])

  useEffect(() => {
    reload()
    const timer = window.setInterval(reload, POLL_MS)
    return () => window.clearInterval(timer)
  }, [reload])

  return { state }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
