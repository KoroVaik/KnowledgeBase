import { apiFetch, readErrorMessage } from './http'

/** A note made from one uploaded file, or one written from many of those. */
export type NoteKind = 'Source' | 'Synthesis'

/** Mirrors NoteSummaryResponse in backend/Controllers/Notes (ASP.NET serialises camelCase). */
export interface NoteSummary {
  id: string
  title: string
  category: string
  kind: NoteKind
  /** A source note is deleted together with its file, so this is null only for a binned one. */
  sourceAssetId: string | null
  /** Kept when the file goes, so the entry in the bin can still name it. */
  sourceFileName: string | null
  createdAtUtc: string
  updatedAtUtc: string
  /** Set means binned: out of the listing, still there to restore or point a link at. */
  deletedAtUtc: string | null
}

/**
 * What one [[title]] in a body points at. The body itself never says - it holds the same text
 * whatever happens to the target - so the server works this out per read.
 */
export type LinkState = 'resolved' | 'deleted' | 'missing'

export interface NoteLinkState {
  title: string
  state: LinkState
  targetId: string | null
}

/** Mirrors NoteResponse in backend/Controllers/Notes: the summary plus body and link states. */
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

/** Every live note, newest first. No body - the front polls this on load. */
export async function fetchNotes(): Promise<NoteSummary[]> {
  const response = await apiFetch('/api/notes')

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

/**
 * Moves the note to the bin. The file it was made from goes too unless told otherwise - the
 * note is a description of that file, and a description of a file that is gone describes
 * nothing. Keeping the file puts it back in the unprocessed state, ready to run again.
 */
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

/**
 * Runs the pipeline over this note's file again. The note stays until the new one is ready,
 * then moves to the bin while the fresh one takes its place - links included.
 */
export async function processNoteAgain(id: string): Promise<void> {
  const response = await apiFetch(`${notePath(id)}/process-again`, { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not queue the file'))
  }
}
