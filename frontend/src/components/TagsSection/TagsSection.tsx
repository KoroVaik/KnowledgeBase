import { DraftProtection } from '../../hooks/useDraftProtection'
import { SectionReload } from '../GenericList/SectionReload'
import { GenericList } from '../GenericList/GenericList'
import { useListPresence } from '../GenericList/useListPresence'
import { useState } from 'react'
import type { Tag } from '../../api/tags'
import { notesText } from '../../format'
import { useTagsSection } from './useTagsSection'
import { ConfirmedTags } from './ConfirmedTags'
import { TagSearchPicker } from '../TagSearchPicker/TagSearchPicker'
import { TagHierarchyTree } from '../TagHierarchyTree/TagHierarchyTree'
import { TagHierarchyGraph } from '../TagHierarchyGraph/TagHierarchyGraph'
import { TagPlacementRow } from '../TagPlacementSuggestions/TagPlacementRow'
import type { Placement } from '../TagPlacementSuggestions/TagPlacementSuggestions'
import { useTagPlacementSuggestions } from '../TagPlacementSuggestions/useTagPlacementSuggestions'
import { TagReviewRow } from './TagReviewRow'
import { LastRunHint } from './LastRunHint'
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
  const { collapsed, toggle } = useCollapsibleSection('tags')
  const {
    state,
    refresh,
    reloadSection,
    placementRevision,
    busy,
    queued,
    error,
    notice,
    remove,
    merge,
    submitReview,
    submitPlacement,
    synthesiseTagNote,
    suggestForReview,
    buildIndex,
    addParent,
    removeParent,
  } = useTagsSection(collapsed)
  const { collapsed: reviewCollapsed, toggle: toggleReview } = useCollapsibleSection('tags:review')
  const { collapsed: hierarchyCollapsed, toggle: toggleHierarchy } = useCollapsibleSection('tags:hierarchy')
  const [hierarchyView, setHierarchyView] = useState<'tree' | 'graph'>('tree')
  const placementIds =
    state.status === 'ready'
      ? state.tags.filter((tag) => tag.confirmed && tag.hasPendingPlacementSuggestion).map((tag) => tag.id)
      : []
  const { byId: placementSuggestions, error: placementError } = useTagPlacementSuggestions(placementIds, placementRevision)
  const reviewPresent = useListPresence(state.status === 'ready' &&
    (state.tags.some(tag => !tag.confirmed) || state.tags.filter(tag => tag.confirmed).length >= 2))
  const confirmedPresent = useListPresence(state.status === 'ready' && state.tags.some(tag => tag.confirmed))

  if (state.status === 'loading') {
    return <section className="tags" {...refresh.bind}><h2><button type="button" className="section-toggle" aria-expanded={!collapsed} onClick={toggle}>Tags</button></h2>{!collapsed && <p>Loading…</p>}</section>
  }

  if (state.status === 'error') {
    return (
      <section className="tags" {...refresh.bind}>
        <h2><button type="button" className="section-toggle" aria-expanded={!collapsed} onClick={toggle}>Tags</button></h2>
        <SectionReload {...refresh} error={state.message} reload={reloadSection} />
        <p className="notes-error" role="alert">
          {state.message}
        </p>
      </section>
    )
  }

  const { tags } = state
  const latestTags = refresh.latest.status === 'ready' ? refresh.latest.tags : tags
  const known = new Set(tags.map(tag => tag.id))
  const reviewUpdates = refresh.additions(tags, latestTags.filter(tag => known.has(tag.id) || !tag.confirmed))
  const confirmedUpdates = refresh.additions(tags, latestTags.filter(tag => known.has(tag.id) || tag.confirmed))
  const tagsById = new Map(tags.map((tag) => [tag.id, tag]))
  const toReview = tags
    .filter((tag) => !tag.confirmed)
    .slice()
    .sort(
      (a, b) =>
        Number(a.possiblyCombined) - Number(b.possiblyCombined) ||
        reviewRank(a, tagsById) - reviewRank(b, tagsById) ||
        b.noteCount - a.noteCount,
    )
  const confirmed = tags.filter((tag) => tag.confirmed)
  const suggestionsQueued = queued.includes('suggest-merges') || queued.includes('suggest-hierarchy')
  // Confirmed only - an unconfirmed tag's own pending suggestion already shows inline in its
  // review row via TagParentOptions; including it here too would show the same guess twice.
  // Unconfirmed children dropped for the same reason: that child's own review row already offers
  // this exact parent, so the confirmed side would repeat it.
  const toPlace = confirmed
    .filter((tag) => tag.hasPendingPlacementSuggestion)
    .flatMap((tag): Placement[] => {
      const fetched = placementSuggestions.get(tag.id)
      if (fetched === undefined) {
        return []
      }

      const suggestions = {
        ...fetched,
        suggestedChildren: fetched.suggestedChildren.filter((child) => tagsById.get(child.id)?.confirmed === true),
      }

      return suggestions.suggestedParents.length === 0 && suggestions.suggestedChildren.length === 0
        ? []
        : [{ tag, suggestions }]
    })

  // Confirmed tags only now - an unconfirmed tag's row is TagReviewRow below, which needs its
  // own per-row decision state (a plain render function like this one can't hold hooks).
  function row(tag: Tag) {
    const suggestion = tag.suggestedMergeIntoId !== null ? tagsById.get(tag.suggestedMergeIntoId) : undefined

    return (
      <li key={tag.id} className="tags-row" aria-busy={busy === tag.id}>
        <span className="tag-chip chip-compact">{tag.name}</span>

        <span className="tags-count">{notesText(tag.noteCount)}</span>

        <span className="tags-actions">
          <TagSearchPicker
            source={tag}
            suggestion={suggestion}
            ariaLabel={`Merge ${tag.name} into another tag`}
            disabled={busy !== null}
            onPick={(into) => merge(tag, into)}
            chip
          />

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
            {busy === tag.id ? 'Saving…' : 'Delete'}
          </button>
        </span>
      </li>
    )
  }

  return (
    <DraftProtection value={refresh.registerDraft}><section className="tags" {...refresh.bind}>
      <h2>
        <button type="button" className="section-toggle" aria-expanded={!collapsed} onClick={toggle}>
          <span className="section-toggle-caret" aria-hidden="true">▾</span>
          Tags
          <span className="section-count">{tags.length}</span>
        </button>
      </h2>

      {!collapsed && (
        <>
          <SectionReload {...refresh} reload={reloadSection} />
          <div className="tags-head-actions">
            <button
              type="button"
              className="btn btn-sm tags-suggest-btn"
              onClick={suggestForReview}
              disabled={busy !== null || tags.length < 2 || suggestionsQueued}
              title="Re-check unreviewed tags for duplicates, and find a parent for any tag that has none yet"
            >
              {suggestionsQueued ? (
                'Suggestions queued'
              ) : (
                <>
                  Suggest for review
                  <LastRunHint completedAtUtc={state.lastSuggestRunUtc} />
                </>
              )}
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

          {(reviewPresent || reviewUpdates.count > 0) && (
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
              </h3>
              {!reviewCollapsed && (
                <>
                      {placementError !== null && <p className="notes-error" role="alert">{placementError}</p>}
                      <GenericList
                        animated
                        listId="tags:review"
                        updates={reviewUpdates}
                        items={[
                          ...toReview.map(tag => ({ kind: 'tag' as const, tag })),
                          ...toPlace.map(placement => ({ kind: 'placement' as const, placement })),
                        ]}
                      >{shownItems => <ul className="tags-list">
                        {shownItems.map(item => item.kind === 'tag' ? (
                          <TagReviewRow
                            key={item.tag.id}
                            tag={item.tag}
                            tagsById={tagsById}
                            disabled={busy !== null}
                            busy={busy === item.tag.id}
                            queued={queued}
                            onMerge={merge}
                            onSynthesise={synthesiseTagNote}
                            onDelete={remove}
                            onSubmit={submitReview}
                          />
                        ) : (
                          <TagPlacementRow
                            key={item.placement.tag.id}
                            tag={item.placement.tag}
                            suggestions={item.placement.suggestions}
                            tagsById={tagsById}
                            disabled={busy !== null}
                            busy={busy === item.placement.tag.id}
                            onMerge={merge}
                            onSubmit={submitPlacement}
                          />
                        ))}
                      </ul>}</GenericList>

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

          {(confirmedPresent || confirmedUpdates.count > 0) && <ConfirmedTags tags={confirmed} renderRow={row} updates={confirmedUpdates} latestTags={latestTags.filter(tag => tag.confirmed)} revision={placementRevision} updatesFor={ids => refresh.additions(confirmed, latestTags.filter(tag => tag.confirmed && ids.has(tag.id)))} />}
          {tags.length === 0 && <p className="tags-empty">No tags yet.</p>}

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
    </section></DraftProtection>
  )
}
