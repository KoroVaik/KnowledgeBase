import { useState } from 'react'
import { formatDateTime } from '../../format'
import { useJobsSection } from './useJobsSection'
import type { ActiveJob } from '../../api/jobs'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import './JobsSection.css'

const KIND_LABELS: Record<string, string> = {
  BuildSourceNote: 'Processing file',
  BuildSynthesis: 'Building synthesis',
  GroupTags: 'Grouping tags',
  SuggestTagParents: 'Suggesting tag parents',
}

export function JobsSection() {
  const { state } = useJobsSection()
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
          <JobKindHint jobId={job.id} description={job.kindDescription} />
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

function JobKindHint({ jobId, description }: { jobId: string; description: string }) {
  const [open, setOpen] = useState(false)
  const tooltipId = `job-hint-${jobId}`

  return (
    <span className="job-hint">
      {/* Explicit open state on click: a tap on mobile Safari never focuses a button, so the
          hover/focus CSS alone would leave touch screens without the tooltip. */}
      <button
        type="button"
        className="job-hint-icon"
        aria-label="What this job does"
        aria-describedby={tooltipId}
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
        onBlur={() => setOpen(false)}
      >
        i
      </button>
      <span id={tooltipId} role="tooltip" className={open ? 'job-hint-tooltip job-hint-tooltip-open' : 'job-hint-tooltip'}>
        {description}
      </span>
    </span>
  )
}
