import type { Tag } from '../../api/tags'
import { notesText } from '../../format'
import { useTagsSection } from './useTagsSection'
import { ConfirmedTags } from './ConfirmedTags'
import { TagMergeOptions } from './TagMergeOptions'
import { TagPicker } from '../TagPicker/TagPicker'
import { TagHierarchyTree } from '../TagHierarchyTree/TagHierarchyTree'
import { TagPlacementSuggestions } from '../TagPlacementSuggestions/TagPlacementSuggestions'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import './TagsSection.css'

const MIN_NOTES = 2

export function TagsSection() {
  const {
    state,
    busy,
    queued,
    error,
    notice,
    remove,
    merge,
    confirm,
    synthesiseTagNote,
    suggestMerges,
    suggestHierarchy,
    buildIndex,
    addParent,
    removeParent,
    acceptSuggestion,
    rejectSuggestion,
  } = useTagsSection()
  const { collapsed, toggle } = useCollapsibleSection('tags')
  const { collapsed: reviewCollapsed, toggle: toggleReview } = useCollapsibleSection('tags:review')
  const { collapsed: placeCollapsed, toggle: togglePlace } = useCollapsibleSection('tags:place')
  const { collapsed: hierarchyCollapsed, toggle: toggleHierarchy } = useCollapsibleSection('tags:hierarchy')

  if (state.status === 'loading') {
    return null
  }

  if (state.status === 'error') {
    return (
      <section className="tags">
        <h2>Tags</h2>
        <p className="notes-error" role="alert">
          {state.message}
        </p>
      </section>
    )
  }

  const { tags } = state
  const toReview = tags.filter((tag) => !tag.confirmed)
  const confirmed = tags.filter((tag) => tag.confirmed)
  const toPlace = tags.filter((tag) => tag.hasPendingPlacementSuggestion)

  function row(tag: Tag) {
    const suggestion = tags.find((other) => other.id === tag.suggestedMergeIntoId)

    return (
      <li key={tag.id} className="tags-row">
        <span
          className={
            tag.confirmed ? 'tag-chip chip-compact' : 'tag-chip tag-chip-unconfirmed chip-compact chip-review'
          }
        >
          {tag.name}
        </span>

        {!tag.confirmed && suggestion !== undefined ? (
          <TagMergeOptions
            tag={tag}
            suggestion={suggestion}
            disabled={busy !== null}
            onPick={(into) => merge(tag, into)}
          />
        ) : (
          <TagPicker
            source={tag}
            suggestion={suggestion}
            ariaLabel={`Merge ${tag.name} into another tag`}
            disabled={busy !== null}
            onPick={(into) => merge(tag, into)}
          />
        )}

        <span className="tags-count">{notesText(tag.noteCount)}</span>

        <span className="tags-actions">
          {!tag.confirmed && (
            <button
              type="button"
              className="btn btn-xs"
              onClick={() => confirm(tag)}
              disabled={busy !== null}
            >
              Confirm
            </button>
          )}

          {tag.noteCount >= MIN_NOTES && (
            <button
              type="button"
              className="btn btn-xs"
              onClick={() => synthesiseTagNote(tag)}
              disabled={busy !== null}
            >
              {queued.includes(tag.name) ? 'Queued' : 'Synthesise'}
            </button>
          )}

          <button type="button" className="btn btn-xs" onClick={() => remove(tag)} disabled={busy !== null}>
            Delete
          </button>
        </span>
      </li>
    )
  }

  return (
    <section className="tags">
      <h2>
        <button type="button" className="section-toggle" aria-expanded={!collapsed} onClick={toggle}>
          <span className="section-toggle-caret" aria-hidden="true">▾</span>
          Tags
          <span className="section-count">{tags.length}</span>
        </button>
      </h2>

      {!collapsed && (
        <>
          <div className="tags-head-actions">
            <button
              type="button"
              className="btn btn-sm"
              onClick={suggestMerges}
              disabled={busy !== null || toReview.length === 0 || tags.length < 2}
              title="Re-check every unreviewed tag against the whole vocabulary"
            >
              {queued.includes('suggest-merges') ? 'Suggestions queued' : 'Suggest merges'}
            </button>

            <button
              type="button"
              className="btn btn-sm"
              onClick={suggestHierarchy}
              disabled={busy !== null || confirmed.length < 2}
              title="Find a parent for every confirmed tag that has none yet"
            >
              {queued.includes('suggest-hierarchy') ? 'Placement queued' : 'Suggest hierarchy'}
            </button>

            <button
              type="button"
              className="btn btn-sm"
              onClick={buildIndex}
              disabled={busy !== null || state.synthesisCount < MIN_NOTES}
              title={
                state.synthesisCount < MIN_NOTES
                  ? 'Needs at least two synthesis notes'
                  : 'Merge every synthesis note into one Contents note'
              }
            >
              {queued.includes('index') ? 'Index queued' : 'Build index'}
            </button>
          </div>

          {error !== null && (
            <p className="notes-error" role="alert">
              {error}
            </p>
          )}

          {error === null && notice !== null && (
            <p className="tags-notice" role="status">
              {notice}
            </p>
          )}

          {tags.length === 0 && <p className="tags-empty">No tags yet.</p>}

          {toReview.length > 0 && (
            <div className="subsection-panel">
              <h3 className="tags-subhead">
                <button
                  type="button"
                  className="subsection-toggle"
                  aria-expanded={!reviewCollapsed}
                  onClick={toggleReview}
                >
                  <span className="section-toggle-caret" aria-hidden="true">▾</span>
                  To review
                  <span className="section-count">{toReview.length}</span>
                </button>
              </h3>
              {!reviewCollapsed && <ul className="tags-list">{toReview.map(row)}</ul>}
            </div>
          )}

          {confirmed.length >= 2 && (
            <div className="subsection-panel">
              <h3 className="tags-subhead">
                <button
                  type="button"
                  className="subsection-toggle"
                  aria-expanded={!placeCollapsed}
                  onClick={togglePlace}
                >
                  <span className="section-toggle-caret" aria-hidden="true">▾</span>
                  To place
                  {toPlace.length > 0 && <span className="section-count">{toPlace.length}</span>}
                </button>
              </h3>
              {!placeCollapsed &&
                (toPlace.length > 0 ? (
                  <TagPlacementSuggestions
                    tags={toPlace}
                    disabled={busy !== null}
                    onAccept={acceptSuggestion}
                    onReject={rejectSuggestion}
                  />
                ) : (
                  <p className="tags-empty">
                    No pending placements. Click “Suggest hierarchy” above, then check back once the
                    worker has run.
                  </p>
                ))}
            </div>
          )}

          {confirmed.length > 0 && <ConfirmedTags tags={confirmed} renderRow={row} />}

          {confirmed.length > 0 && (
            <div className="subsection-panel">
              <h3 className="tags-subhead">
                <button
                  type="button"
                  className="subsection-toggle"
                  aria-expanded={!hierarchyCollapsed}
                  onClick={toggleHierarchy}
                >
                  <span className="section-toggle-caret" aria-hidden="true">▾</span>
                  Hierarchy
                </button>
              </h3>
              {!hierarchyCollapsed && (
                <TagHierarchyTree
                  tags={confirmed}
                  disabled={busy !== null}
                  onAddParent={addParent}
                  onRemoveParent={removeParent}
                />
              )}
            </div>
          )}
        </>
      )}
    </section>
  )
}
