import { formatDateTime } from '../../format'
import { useJobsSection } from './useJobsSection'
import type { ActiveJob, FailedJob } from '../../api/jobs'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import { InfoHint } from '../InfoHint/InfoHint'
import './JobsSection.css'

const KIND_LABELS: Record<string, string> = {
  BuildSourceNote: 'Processing file',
  BuildSynthesis: 'Building synthesis',
  GroupTags: 'Grouping tags',
  SuggestTagParents: 'Suggesting tag parents',
}

export function JobsSection() {
  const { state, retrying, retryError, retry } = useJobsSection()
  const { collapsed, toggle } = useCollapsibleSection('jobs')

  return (
    <section className="jobs">
      <h2>
        <button type="button" className="section-toggle" aria-expanded={!collapsed} onClick={toggle}>
          <span className="section-toggle-caret" aria-hidden="true">▾</span>
          Jobs
          {state.status === 'ready' && <span className="section-count">{state.jobs.length}</span>}
        </button>
      </h2>

      {!collapsed && (
        <>
          {state.status === 'loading' && <p>Loading…</p>}

          {state.status === 'error' && (
            <p className="notes-error" role="alert">
              {state.message}
            </p>
          )}

          {state.status === 'ready' && state.jobs.length === 0 && <p>No active jobs.</p>}

          {state.status === 'ready' && state.jobs.length > 0 && (
            <ul className="jobs-list">
              {state.jobs.map((job) => (
                <JobRow key={job.id} job={job} />
              ))}
            </ul>
          )}

          {state.status === 'ready' && state.failed.length > 0 && (
            <div className="jobs-failed">
              <div className="jobs-failed-head">
                <h3>
                  Failed <span className="section-count">{state.failed.length}</span>
                </h3>
                <button
                  type="button"
                  className="btn btn-sm"
                  onClick={() => void retry('all')}
                  disabled={retrying !== null}
                >
                  {retrying === 'all' ? 'Retrying…' : 'Retry all'}
                </button>
              </div>

              {retryError !== null && (
                <p className="notes-error" role="alert">
                  {retryError}
                </p>
              )}

              <ul className="jobs-list">
                {state.failed.map((job) => (
                  <FailedJobRow
                    key={job.id}
                    job={job}
                    retrying={retrying === job.id}
                    disabled={retrying !== null}
                    onRetry={() => void retry(job.id)}
                  />
                ))}
              </ul>
            </div>
          )}
        </>
      )}
    </section>
  )
}

function JobRow({ job }: { job: ActiveJob }) {
  const running = job.status === 'Running'

  return (
    <li className="job">
      <div className="job-head">
        <span className="job-kind">
          {KIND_LABELS[job.kind] ?? job.kind}
          <InfoHint id={`job-${job.id}`} label="What this job does" description={job.kindDescription} />
          {job.assetFileName !== null && ` — ${job.assetFileName}`}
        </span>
        <span className={running ? 'job-status job-status-running' : 'job-status job-status-pending'}>
          {running ? 'Running' : 'Queued'}
        </span>
      </div>
      <div className="job-meta">
        Queued {formatDateTime(job.createdAtUtc)}
        {job.startedAtUtc !== null && ` · Started ${formatDateTime(job.startedAtUtc)}`}
        {job.attempts > 0 && ` · Attempt ${job.attempts}`}
      </div>
      {/* Set when a previous attempt failed and the worker handed the job back to the queue. */}
      {job.error !== null && <div className="job-error">Last attempt failed: {job.error}</div>}
    </li>
  )
}

function FailedJobRow({
  job,
  retrying,
  disabled,
  onRetry,
}: {
  job: FailedJob
  retrying: boolean
  disabled: boolean
  onRetry: () => void
}) {
  return (
    <li className="job">
      <div className="job-head">
        <span className="job-kind">
          {KIND_LABELS[job.kind] ?? job.kind}
          <InfoHint id={`job-${job.id}`} label="What this job does" description={job.kindDescription} />
          {job.assetFileName !== null && ` — ${job.assetFileName}`}
        </span>
        <span className="job-actions">
          <span className="job-status job-status-failed">Failed</span>
          <button type="button" className="btn btn-xs" onClick={onRetry} disabled={disabled}>
            {retrying ? 'Retrying…' : 'Retry'}
          </button>
        </span>
      </div>
      <div className="job-meta">
        Queued {formatDateTime(job.createdAtUtc)}
        {job.completedAtUtc !== null && ` · Failed ${formatDateTime(job.completedAtUtc)}`}
        {` · ${job.attempts} ${job.attempts === 1 ? 'attempt' : 'attempts'}`}
      </div>
      {job.error !== null && <div className="job-error">{job.error}</div>}
    </li>
  )
}
