import type { ReactNode } from 'react'
import type { NoteSummary } from '../../api/notes'

type NoteRowProps = {
  note: NoteSummary
  binned?: boolean
  expanded: boolean
  onToggle: () => void
  meta: ReactNode
  belowHead?: ReactNode
  actions: ReactNode
  body: ReactNode
}

/** The shell shared by an active note row and a binned one: title on its own line, then
 *  meta and actions, then the expanded body. What goes in each slot differs by caller -
 *  `body` arrives already rendered (loading / error / the note's HTML), NoteRow just places it. */
export function NoteRow({ note, binned = false, expanded, onToggle, meta, belowHead, actions, body }: NoteRowProps) {
  return (
    <li className={binned ? 'note note-binned' : 'note'} data-note-row={note.id}>
      <div className="note-row">
        <button type="button" className="note-head" aria-expanded={expanded} onClick={onToggle}>
          <span className="note-title">{note.title}</span>
          <span className="note-meta">{meta}</span>
          {belowHead}
        </button>

        <div className="note-actions">{actions}</div>
      </div>

      {expanded && body}
    </li>
  )
}
