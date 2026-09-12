import { useState } from 'react'
import type { Tag } from '../../api/tags'
import { notesText } from '../../format'
import { useTagsSection } from './useTagsSection'
import { ConfirmedTags } from './ConfirmedTags'
import { TagPicker } from '../TagPicker/TagPicker'
import { TagHierarchyTree } from '../TagHierarchyTree/TagHierarchyTree'
import { TagHierarchyGraph } from '../TagHierarchyGraph/TagHierarchyGraph'
import { TagPlacementSuggestions } from '../TagPlacementSuggestions/TagPlacementSuggestions'
import { TagSuggestionChip } from '../TagPlacementSuggestions/TagSuggestionChip'
import { TagSuggestionGraph } from '../TagSuggestionGraph/TagSuggestionGraph'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import './TagsSection.css'

const MIN_NOTES = 2

// A tag with no pending parent suggestion is likely a root/abstract one - reviewing it blocks on
// nothing. One whose suggested parent is already confirmed is ready to decide with full context;
// one whose suggested parent is itself still unconfirmed is better left for after that parent's
// own fate is settled. Confirming a parent tag re-ranks its children up on the next reload -
// that is what gives the effect of "abstract tags first, their children follow".
function reviewRank(tag: Tag, tagsById: Map<string, Tag>): number {
  if (tag.pendingParentSuggestions.length === 0) {
    return 0
  }

  const aParentIsAlreadyConfirmed = tag.pendingParentSuggestions.some(
    (suggestion) => tagsById.get(suggestion.parentId)?.confirmed === true,
  )

  return aParentIsAlreadyConfirmed ? 1 : 2
}

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
    confirmWithParent,
    confirmWithParentFlipped,
    flipSuggestion,
    synthesiseTagNote,
    suggestForReview,
    buildIndex,
    addParent,
    removeParent,
    acceptSuggestion,
    rejectSuggestion,
  } = useTagsSection()
  const { collapsed, toggle } = useCollapsibleSection('tags')
  const { collapsed: reviewCollapsed, toggle: toggleReview } = useCollapsibleSection('tags:review')
  const { collapsed: hierarchyCollapsed, toggle: toggleHierarchy } = useCollapsibleSection('tags:hierarchy')
  const [hierarchyView, setHierarchyView] = useState<'tree' | 'graph'>('tree')
  const [reviewView, setReviewView] = useState<'list' | 'graph'>('list')

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
  const tagsById = new Map(tags.map((tag) => [tag.id, tag]))
  const toReview = tags
    .filter((tag) => !tag.confirmed)
    .slice()
    .sort((a, b) => reviewRank(a, tagsById) - reviewRank(b, tagsById) || b.noteCount - a.noteCount)
  const confirmed = tags.filter((tag) => tag.confirmed)
  // Confirmed only - an unconfirmed tag's own pending suggestion already shows inline in its
  // review row via TagParentOptions; including it here too would show the same guess twice.
  const toPlace = confirmed.filter((tag) => tag.hasPendingPlacementSuggestion)

  function row(tag: Tag) {
    const suggestion = tags.find((other) => other.id === tag.suggestedMergeIntoId)

    const parentCandidates = tag.confirmed
      ? []
      : tag.pendingParentSuggestions.flatMap((pending) => {
          const parent = tagsById.get(pending.parentId)
          return parent === undefined ? [] : [{ parent, confidence: pending.confidence }]
        })

    const showGraph = reviewView === 'graph' && parentCandidates.length > 0

    const mergeControl = (
      <TagPicker
        source={tag}
        suggestion={suggestion}
        ariaLabel={`Merge ${tag.name} into another tag`}
        disabled={busy !== null}
        onPick={(into) => merge(tag, into)}
        chip
      />
    )

    return (
      <li key={tag.id} className="tags-row">
        {!showGraph && (
          <span
            className={
              tag.confirmed ? 'tag-chip chip-compact' : 'tag-chip tag-chip-unconfirmed chip-compact chip-review'
            }
          >
            {tag.name}
          </span>
        )}

        {parentCandidates.length > 0 &&
          (() => {
            const candidates = parentCandidates.map(({ parent, confidence }) => ({
              id: parent.id,
              name: parent.name,
              confidence,
              confirmed: parent.confirmed,
            }))
            const onAcceptParent = (parentId: string) => {
              const candidate = parentCandidates.find(({ parent }) => parent.id === parentId)
              if (candidate !== undefined) {
                confirmWithParent(tag, candidate.parent)
              }
            }
            const onRejectParent = (parentId: string) => rejectSuggestion(tag.id, parentId)

            if (reviewView !== 'graph') {
              return (
                <TagSuggestionChip
                  label="Parent"
                  direction="parent"
                  candidates={candidates}
                  disabled={busy !== null}
                  onAccept={onAcceptParent}
                  onReject={onRejectParent}
                />
              )
            }

            // Flip only offered with exactly one candidate - with more, swapping the center
            // would leave the other candidates' edges pointing at a center that just changed.
            const sole = parentCandidates.length === 1 ? parentCandidates[0] : null

            return (
              <TagSuggestionGraph
                centerName={tag.name}
                centerConfirmed={tag.confirmed}
                parents={candidates}
                childCandidates={[]}
                disabled={busy !== null}
                onAcceptParent={onAcceptParent}
                onRejectParent={onRejectParent}
                onFlipAcceptParent={sole !== null ? () => confirmWithParentFlipped(tag, sole.parent) : undefined}
              />
            )
          })()}

        <span className="tags-count">{notesText(tag.noteCount)}</span>

        <span className="tags-actions">
          {mergeControl}

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
              onClick={suggestForReview}
              disabled={busy !== null || tags.length < 2}
              title="Re-check unreviewed tags for duplicates, and find a parent for any tag that has none yet"
            >
              {queued.includes('suggest-merges') || queued.includes('suggest-hierarchy')
                ? 'Suggestions queued'
                : 'Suggest for review'}
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

          {(toReview.length > 0 || confirmed.length >= 2) && (
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
                  <span className="section-count">{toReview.length + toPlace.length}</span>
                </button>

                {!reviewCollapsed && (
                  <div className="tags-view-tabs" role="tablist" aria-label="Review view">
                    <button
                      type="button"
                      role="tab"
                      aria-selected={reviewView === 'list'}
                      className={reviewView === 'list' ? 'tags-view-tab tags-view-tab-active' : 'tags-view-tab'}
                      onClick={() => setReviewView('list')}
                    >
                      List
                    </button>
                    <button
                      type="button"
                      role="tab"
                      aria-selected={reviewView === 'graph'}
                      className={reviewView === 'graph' ? 'tags-view-tab tags-view-tab-active' : 'tags-view-tab'}
                      onClick={() => setReviewView('graph')}
                    >
                      Graph
                    </button>
                  </div>
                )}
              </h3>
              {!reviewCollapsed && (
                <>
                  {(toReview.length > 0 || toPlace.length > 0) && (
                    <ul className="tags-list">
                      {toReview.map(row)}
                      <TagPlacementSuggestions
                        tags={toPlace}
                        tagsById={tagsById}
                        view={reviewView}
                        disabled={busy !== null}
                        onAccept={acceptSuggestion}
                        onReject={rejectSuggestion}
                        onFlip={flipSuggestion}
                        onMerge={merge}
                      />
                    </ul>
                  )}

                  {toReview.length === 0 && toPlace.length === 0 && confirmed.length >= 2 && (
                    <p className="tags-empty">
                      No pending placements. Click “Suggest for review” above, then check back once
                      the worker has run.
                    </p>
                  )}
                </>
              )}
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

                {!hierarchyCollapsed && (
                  <div className="tags-view-tabs" role="tablist" aria-label="Hierarchy view">
                    <button
                      type="button"
                      role="tab"
                      aria-selected={hierarchyView === 'tree'}
                      className={hierarchyView === 'tree' ? 'tags-view-tab tags-view-tab-active' : 'tags-view-tab'}
                      onClick={() => setHierarchyView('tree')}
                    >
                      Tree
                    </button>
                    <button
                      type="button"
                      role="tab"
                      aria-selected={hierarchyView === 'graph'}
                      className={hierarchyView === 'graph' ? 'tags-view-tab tags-view-tab-active' : 'tags-view-tab'}
                      onClick={() => setHierarchyView('graph')}
                    >
                      Graph
                    </button>
                  </div>
                )}
              </h3>
              {!hierarchyCollapsed &&
                (hierarchyView === 'tree' ? (
                  <TagHierarchyTree
                    tags={confirmed}
                    disabled={busy !== null}
                    onAddParent={addParent}
                    onRemoveParent={removeParent}
                  />
                ) : (
                  <TagHierarchyGraph tags={confirmed} />
                ))}
            </div>
          )}
        </>
      )}
    </section>
  )
}
