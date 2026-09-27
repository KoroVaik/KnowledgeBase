import { useDraftProtection } from '../../hooks/useDraftProtection'
import { useSectionRefresh, useVisibleReload } from '../../hooks/useSectionRefresh'
import { useGenericList } from '../GenericList/useGenericList'
import { requestContext, withRequestContext } from '../../diagnostics/diagnostics'
import { useCallback, useRef, useState } from 'react'
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
export function useNotesList(collapsed: boolean) {
  const refresh = useSectionRefresh<ListState>({ status: 'loading' }, value => value.status === 'ready', collapsed)
  const { state, receive: setState, updateLocal, canLoad, setError } = refresh
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

  useDraftProtection(deleting !== null || deleteBusy || busyId !== null, refresh.registerDraft)

  // Two reloads (mount + stream event) can race; only the newest writes. Same guard on the body.
  const latestReload = useRef(0)
  const latestBody = useRef(0)

  const reload = useCallback((acceptedIds: readonly string[] = []) => withRequestContext(requestContext('NotesList'), () => {
    if (!canLoad(acceptedIds)) return
    const reloadId = ++latestReload.current

    // Source notes now live under their file in the Files section; this list is the rest.
    return Promise.all([fetchNotes(['Synthesis', 'Index']), fetchTrash()])
      .then(([notes, trash]) => {
        if (reloadId !== latestReload.current) {
          return
        }

        setState({ status: 'ready', notes, trash }, acceptedIds)
        setError(null)
        setReprocessing((current) => current.filter((id) => notes.some((note) => note.id === id)))
      })
      .catch((error: unknown) => {
        if (reloadId !== latestReload.current) {
          return
        }

        // Fail quietly: stale rows beat blanking them on a flaky connection.
        setError(messageOf(error))
        updateLocal((current) =>
          current.status === 'ready' ? current : { status: 'error', message: messageOf(error) },
        )
      })
  }), [canLoad, setState, updateLocal, setError])

  useResourceChanges('notes', reload)
  useVisibleReload(refresh.enabled, () => reload(), reprocessing.length > 0 ? 4000 : undefined)
  const notes = state.status === 'ready' ? state.notes : []
  const trash = state.status === 'ready' ? state.trash : []
  const incoming = refresh.latest.status === 'ready' ? refresh.latest : { notes: [], trash: [] }
  const notesList = useGenericList(notes, 'notes', '', refresh.additions(notes, incoming.notes))
  const trashList = useGenericList(trash, 'notes:bin', '', refresh.additions(trash, incoming.trash))

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

    if (state.status === 'ready') {
      const noteIndex = state.notes.findIndex(note => note.id === id)
      notesList.reveal(noteIndex)
      const trashIndex = state.trash.findIndex(note => note.id === id)
      if (trashIndex >= 0) {
        setShowTrash(true)
        trashList.reveal(trashIndex)
      }
    }
    window.requestAnimationFrame(() => {
      document.querySelector(`[data-note-row="${id}"]`)?.scrollIntoView({ block: 'nearest' })
    })
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
      await reload([deleting.id])
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
      await reload([note.id])
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

      await reload([note.id])
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setBusyId(null)
    }
  }

  return {
    state,
    refresh,
    reloadSection: () => void refresh.reload(async () => {
      await reload()
      if (expandedId !== null) await open(expandedId)
    }),
    notesList,
    trashList,
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
