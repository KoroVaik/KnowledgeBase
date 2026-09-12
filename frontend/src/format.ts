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

export function notesText(count: number): string {
  return count === 1 ? '1 note' : `${count} notes`
}
