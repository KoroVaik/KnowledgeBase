export function formatSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

/** Short local date + time, no seconds: "10 Sept 2026, 23:01" in the browser's locale. */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

/** Decimal degrees, 4 places (~11 m) - plenty for "where was this taken", not a survey. */
export function formatCoordinates(latitude: number, longitude: number): string {
  return `${latitude.toFixed(4)}, ${longitude.toFixed(4)}`
}

const timeAgoUnits: Array<[Intl.RelativeTimeFormatUnit, number]> = [
  ['year', 365 * 24 * 60 * 60],
  ['month', 30 * 24 * 60 * 60],
  ['day', 24 * 60 * 60],
  ['hour', 60 * 60],
  ['minute', 60],
]

/** "4 hours ago", "yesterday", "just now" - the largest whole unit, rounded down. */
export function formatTimeAgo(iso: string, now: number): string {
  const seconds = Math.max(0, (now - new Date(iso).getTime()) / 1000)
  const format = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })

  for (const [unit, size] of timeAgoUnits) {
    if (seconds >= size) {
      return format.format(-Math.floor(seconds / size), unit)
    }
  }

  return 'just now'
}

export function notesText(count: number): string {
  return count === 1 ? '1 note' : `${count} notes`
}
