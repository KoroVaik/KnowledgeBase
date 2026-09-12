import type { Tag, TagSuggestion } from '../../api/tags'
import { useTagPlacementSuggestions } from './useTagPlacementSuggestions'
import './TagPlacementSuggestions.css'

/** One card per tag with a pending AI placement guess: the tag itself, with any suggested
 *  parents above it and suggested children below - only the sides that actually have a
 *  suggestion get a row, so a tag with just one guess is a single pill and one line, not a
 *  fixed four-slot graph. See "Tag hierarchy" in docs/database.md. */
export function TagPlacementSuggestions({
  tags,
  disabled,
  onAccept,
  onReject,
}: {
  tags: Tag[]
  disabled: boolean
  onAccept: (childId: string, parentId: string) => void
  onReject: (childId: string, parentId: string) => void
}) {
  const taggedIds = tags.map((tag) => tag.id)
  const { byId, error } = useTagPlacementSuggestions(taggedIds)

  function pill(tag: TagSuggestion, onYes: () => void, onNo: () => void) {
    return (
      <div key={tag.id} className="tag-suggestion-pill">
        <span>{tag.name}</span>
        <button type="button" className="tag-suggestion-accept" aria-label={`Accept ${tag.name}`} onClick={onYes} disabled={disabled}>
          ✓
        </button>
        <button type="button" className="tag-suggestion-reject" aria-label={`Reject ${tag.name}`} onClick={onNo} disabled={disabled}>
          ×
        </button>
      </div>
    )
  }

  return (
    <div className="tag-suggestion-cards">
      {error !== null && (
        <p className="notes-error" role="alert">
          {error}
        </p>
      )}

      {tags.map((tag) => {
        const suggestions = byId.get(tag.id)

        if (!suggestions) {
          return null
        }

        return (
          <div key={tag.id} className="tag-suggestion-card">
            {suggestions.suggestedParents.length > 0 && (
              <div className="tag-suggestion-row">
                {suggestions.suggestedParents.map((parent) =>
                  pill(
                    parent,
                    () => onAccept(tag.id, parent.id),
                    () => onReject(tag.id, parent.id),
                  ),
                )}
              </div>
            )}

            <div className="tag-suggestion-center">{tag.name}</div>

            {suggestions.suggestedChildren.length > 0 && (
              <div className="tag-suggestion-row">
                {suggestions.suggestedChildren.map((child) =>
                  pill(
                    child,
                    () => onAccept(child.id, tag.id),
                    () => onReject(child.id, tag.id),
                  ),
                )}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
