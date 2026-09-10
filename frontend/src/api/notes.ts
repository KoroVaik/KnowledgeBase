import { apiFetch, readErrorMessage } from './http'

/** A note made from one uploaded file, or one written from many of those. */
export type NoteKind = 'Source' | 'Synthesis'

/** Mirrors NoteSummaryResponse in backend Controllers/Notes. */
export interface NoteSummary {
  id: string
  title: string
  category: string
  kind: NoteKind
  sourceAssetId: string | null
  /** Kept when the file goes, so the bin entry can still name it. */
  sourceFileName: string | null
  createdAtUtc: string
  updatedAtUtc: string
  /** Set means binned. */
  deletedAtUtc: string | null
}

/** What one [[title]] points at. The body holds the same text either way; the server resolves. */
export type LinkState = 'resolved' | 'deleted' | 'missing'

export interface NoteLinkState {
  title: string
  state: LinkState
  targetId: string | null
}

/** Mirrors NoteResponse in backend Controllers/Notes. */
export interface Note extends NoteSummary {
  body: string
  links: NoteLinkState[]
}

/** Mirrors BacklinkResponse: a note that points at the one being looked at. */
export interface Backlink {
  id: string
  title: string
  kind: NoteKind
}

function notePath(id: string): string {
  return `/api/notes/${encodeURIComponent(id)}`
}

/** Live notes, newest first, no body. `kind` narrows to one kind (Source lives under its file). */
export async function fetchNotes(kind?: NoteKind): Promise<NoteSummary[]> {
  const query = kind === undefined ? '' : `?kind=${kind}`
  const response = await apiFetch(`/api/notes${query}`)

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the notes'))
  }

  return (await response.json()) as NoteSummary[]
}

/** The binned notes, most recently binned first. */
export async function fetchTrash(): Promise<NoteSummary[]> {
  const response = await apiFetch('/api/notes/trash')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the bin'))
  }

  return (await response.json()) as NoteSummary[]
}

/** One note with its Markdown body. Asked for when a row is expanded, not when it renders. */
export async function fetchNote(id: string): Promise<Note> {
  const response = await apiFetch(notePath(id))

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not open the note'))
  }

  return (await response.json()) as Note
}

/** Who points here. Asked for when the delete confirmation opens. */
export async function fetchBacklinks(id: string): Promise<Backlink[]> {
  const response = await apiFetch(`${notePath(id)}/backlinks`)

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the backlinks'))
  }

  return (await response.json()) as Backlink[]
}

/** To the bin. The source file goes too unless `deleteSource` is false, which leaves it
 *  unprocessed and ready to run again. */
export async function deleteNote(id: string, deleteSource: boolean): Promise<void> {
  const response = await apiFetch(`${notePath(id)}?deleteSource=${String(deleteSource)}`, {
    method: 'DELETE',
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Delete failed'))
  }
}

export async function restoreNote(id: string): Promise<void> {
  const response = await apiFetch(`${notePath(id)}/restore`, { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Restore failed'))
  }
}

/** Out of the bin for good. Links pointing here stop saying "deleted" and go blank instead. */
export async function purgeNote(id: string): Promise<void> {
  const response = await apiFetch(`${notePath(id)}/purge`, { method: 'DELETE' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not empty this out of the bin'))
  }
}

/** Re-runs the pipeline over the file. The note stays until the new one is ready, then moves
 *  to the bin while the fresh one takes its place, links included. */
export async function processNoteAgain(id: string): Promise<void> {
  const response = await apiFetch(`${notePath(id)}/process-again`, { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not queue the file'))
  }
}
