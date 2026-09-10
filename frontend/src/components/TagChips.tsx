import type { NoteTag } from '../api/notes'

/** A note's tags. First chip is the primary tag; an unconfirmed one shows grey and dashed,
 *  the same cue as an unresolved wiki-link. Renders nothing when the note is untagged. */
export function TagChips({ tags }: { tags: NoteTag[] }) {
  if (tags.length === 0) {
    return null
  }

  return (
    <span className="tag-chips">
      {tags.map((tag) => (
        <span
          key={tag.name}
          className={tag.confirmed ? 'tag-chip' : 'tag-chip tag-chip-unconfirmed'}
          title={tag.confirmed ? undefined : 'Suggested by the pipeline, not yet confirmed'}
        >
          {tag.name}
        </span>
      ))}
    </span>
  )
}
