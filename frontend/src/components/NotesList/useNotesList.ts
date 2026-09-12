import { useCallback, useEffect, useRef, useState } from 'react'
import {
  deleteNote,
  fetchNote,
  fetchNotes,
  fetchTrash,
  processNoteAgain,
  purgeNote,
  restoreNote,
} from '../../api/notes'
import type { Note, NoteSummary } from '../../api/notes'
import { useResourceChanges } from '../../hooks/useResourceChanges'

export type ListState =
  | { status: 'loading' }
  | { status: 'ready'; notes: NoteSummary[]; trash: NoteSummary[] }
  | { status: 'error'; message: string }

export type BodyState =
  | { status: 'loading' }
  | { status: 'ready'; note: Note }
  | { status: 'error'; message: string }

/** All state and server calls behind the notes list: loading, expand/collapse, and the
 *  delete/restore/purge/process-again actions. `NotesList` only turns this into markup. */
export function useNotesList() {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [expandedId, setExpandedId] = useState<string | null>(null)
  const [body, setBody] = useState<BodyState | null>(null)
  const [showTrash, setShowTrash] = useState(false)
  const [deleting, setDeleting] = useState<NoteSummary | null>(null)
  const [deleteBusy, setDeleteBusy] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  // Notes waiting for a fresh version. The worker has no change stream to the browser, so
  // poll until the watched id disappears - which is what being replaced looks like.
  const [reprocessing, setReprocessing] = useState<string[]>([])

  // Two reloads (mount + stream event) can race; only the newest writes. Same guard on the body.
  const latestReload = useRef(0)
  const latestBody = useRef(0)

  const reload = useCallback(() => {
    const reloadId = ++latestReload.current

    // Source notes now live under their file in the Files section; this list is the rest.
    void Promise.all([fetchNotes(['Synthesis', 'Index']), fetchTrash()])
      .then(([notes, trash]) => {
        if (reloadId !== latestReload.current) {
          return
        }

        setState({ status: 'ready', notes, trash })
        setReprocessing((current) => current.filter((id) => notes.some((note) => note.id === id)))
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

  useEffect(reload, [reload])

  useResourceChanges('notes', reload)

  useEffect(() => {
    if (reprocessing.length === 0) {
      return
    }

    const timer = window.setInterval(reload, 4000)
    return () => window.clearInterval(timer)
  }, [reprocessing, reload])

  const open = useCallback(async (id: string) => {
    const bodyId = ++latestBody.current
    setExpandedId(id)
    setBody({ status: 'loading' })

    try {
      const note = await fetchNote(id)
      if (bodyId === latestBody.current) {
        setBody({ status: 'ready', note })
      }
    } catch (error) {
      if (bodyId === latestBody.current) {
        setBody({ status: 'error', message: messageOf(error) })
      }
    }
  }, [])

  async function toggle(id: string) {
    if (id === expandedId) {
      setExpandedId(null)
      setBody(null)
      return
    }

    await open(id)
  }

  /** One handler on the container rather than one per link: the body is rendered HTML. */
  function followLink(event: React.MouseEvent<HTMLDivElement>) {
    const target = (event.target as HTMLElement).closest<HTMLElement>('[data-note-id]')
    const id = target?.dataset.noteId

    if (id === undefined) {
      return
    }

    // The target may be off screen, where opening it would look like nothing happened.
    document.querySelector(`[data-note-row="${id}"]`)?.scrollIntoView({ block: 'nearest' })
    void open(id)
  }

  async function handleProcessAgain(note: NoteSummary) {
    setActionError(null)
    setBusyId(note.id)

    try {
      await processNoteAgain(note.id)
      setReprocessing((current) => [...current, note.id])
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusyId(null)
    }
  }

  async function handleDelete(deleteSource: boolean) {
    if (deleting === null) {
      return
    }

    setActionError(null)
    setDeleteBusy(true)

    try {
      await deleteNote(deleting.id, deleteSource)

      if (expandedId === deleting.id) {
        setExpandedId(null)
        setBody(null)
      }

      setDeleting(null)
      reload()
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setDeleteBusy(false)
    }
  }

  async function handleRestore(note: NoteSummary) {
    setActionError(null)
    setBusyId(note.id)

    try {
      await restoreNote(note.id)
      reload()
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusyId(null)
    }
  }

  async function handlePurge(note: NoteSummary) {
    if (
      !window.confirm(
        `Delete “${note.title}” permanently? Links to it will stop saying it was deleted and read as “no such note” instead.`,
      )
    ) {
      return
    }

    setActionError(null)
    setBusyId(note.id)

    try {
      await purgeNote(note.id)

      if (expandedId === note.id) {
        setExpandedId(null)
        setBody(null)
      }

      reload()
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusyId(null)
    }
  }

  return {
    state,
    expandedId,
    body,
    showTrash,
    setShowTrash,
    deleting,
    setDeleting,
    deleteBusy,
    busyId,
    actionError,
    reprocessing,
    toggle,
    followLink,
    handleProcessAgain,
    handleDelete,
    handleRestore,
    handlePurge,
  }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
