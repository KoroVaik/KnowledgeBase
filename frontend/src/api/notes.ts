import { apiFetch, readErrorMessage } from './http'

/** Mirrors NoteSummaryResponse in backend/Controllers/Notes (ASP.NET serialises camelCase). */
export interface NoteSummary {
  id: string
  title: string
  category: string
  createdAtUtc: string
  updatedAtUtc: string
}

/** Mirrors NoteResponse in backend/Controllers/Notes: the summary plus the Markdown body. */
export interface Note extends NoteSummary {
  body: string
  sourceAssetId: string | null
}

/** Every note, newest first. No body - the front polls this on load. */
export async function fetchNotes(): Promise<NoteSummary[]> {
  const response = await apiFetch('/api/notes')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the notes'))
  }

  return (await response.json()) as NoteSummary[]
}

/** One note with its Markdown body. Asked for when a row is expanded, not when it renders. */
export async function fetchNote(id: string): Promise<Note> {
  const response = await apiFetch(`/api/notes/${encodeURIComponent(id)}`)

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not open the note'))
  }

  return (await response.json()) as Note
}
