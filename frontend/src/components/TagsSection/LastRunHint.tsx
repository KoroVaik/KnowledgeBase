import { formatDateTime, formatTimeAgo } from '../../format'
import { useNow } from '../../hooks/useNow'

// A component of its own so the once-a-minute tick re-renders this line, not the whole section.
export function LastRunHint({ completedAtUtc }: { completedAtUtc: string | null }) {
  const now = useNow(60_000)

  if (completedAtUtc === null) {
    return <span className="tags-last-run">never run</span>
  }

  return (
    <span className="tags-last-run" title={formatDateTime(completedAtUtc)}>
      last run {formatTimeAgo(completedAtUtc, now)}
    </span>
  )
}
