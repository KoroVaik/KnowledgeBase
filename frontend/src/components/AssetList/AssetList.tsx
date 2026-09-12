import type { AssetSummary } from '../../api/assets'
import { formatCoordinates, formatDateTime, formatSize } from '../../format'
import { useAssetList } from './useAssetList'
import type { StatusValue } from './useAssetList'
import { FilePanel } from '../FilePanel/FilePanel'
import { useCollapsibleSection } from '../../hooks/useCollapsibleSection'
import './AssetList.css'

type StatusTone = 'info' | 'ok' | 'warn' | 'error' | 'pending' | 'running'

const STATUS_FILTER_OPTIONS: { value: StatusValue; label: string; tone: StatusTone }[] = [
  { value: 'Pending', label: 'Pending', tone: 'pending' },
  { value: 'Running', label: 'Running', tone: 'running' },
  { value: 'Done', label: 'Done', tone: 'ok' },
  { value: 'Failed', label: 'Failed', tone: 'error' },
  { value: 'Skipped', label: 'Skipped', tone: 'warn' },
  { value: 'None', label: 'No status', tone: 'info' },
]

interface AssetListProps {
  /** Bumped after a successful upload to reload the list - covers a down change stream. */
  reloadToken: number
  /** From /api/features: threshold for the "possibly too large" hint on text files. */
  maxSourceChars: number
}

export function AssetList({ reloadToken, maxSourceChars }: AssetListProps) {
  const {
    state,
    expanded,
    setExpanded,
    actionError,
    selected,
    bulkDeleting,
    selectedStatuses,
    toggleStatus,
    statusCounts,
    visibleAssets,
    shownAssets,
    hasMore,
    showMore,
    selectedAssets,
    allSelected,
    reload,
    removeRow,
    toggleOne,
    toggleAll,
    handleBulkDelete,
  } = useAssetList(reloadToken)
  const { collapsed, toggle } = useCollapsibleSection('assets')

  return (
    <section className="assets">
      <h2>
        <button type="button" className="section-toggle" aria-expanded={!collapsed} onClick={toggle}>
          <span className="section-toggle-caret" aria-hidden="true">▾</span>
          Files
        </button>

        {state.status === 'ready' && (
          <span className="asset-status-counts">
            {STATUS_FILTER_OPTIONS.map(({ value, label, tone }) => {
              const isOn = selectedStatuses.has(value)
              return (
                <button
                  key={value}
                  type="button"
                  className={
                    isOn
                      ? `asset-status-toggle asset-status asset-status-${tone}`
                      : `asset-status-toggle asset-status asset-status-off asset-status-${tone}`
                  }
                  aria-pressed={isOn}
                  onClick={() => toggleStatus(value)}
                >
                  {label} {statusCounts[value]}
                </button>
              )
            })}
          </span>
        )}
      </h2>

      {!collapsed && (
        <>
          {state.status === 'loading' && <p>Loading…</p>}

          {state.status === 'error' && (
            <p className="assets-error" role="alert">
              {state.message}
            </p>
          )}

          {actionError !== null && (
            <p className="assets-error" role="alert">
              {actionError}
            </p>
          )}

          {state.status === 'ready' && state.assets.length === 0 && <p>Nothing uploaded yet.</p>}

          {state.status === 'ready' && state.assets.length > 0 && (
            <>
              <div className="assets-selection">
                <label className="assets-selection-all">
                  <input
                    type="checkbox"
                    checked={allSelected}
                    ref={(node) => {
                      if (node) {
                        node.indeterminate = selectedAssets.length > 0 && !allSelected
                      }
                    }}
                    onChange={toggleAll}
                    disabled={bulkDeleting}
                  />
                  {selectedAssets.length > 0 ? `${selectedAssets.length} selected` : 'Select all'}
                </label>

                {selectedAssets.length > 0 && (
                  <button
                    type="button"
                    className="btn btn-md"
                    onClick={() => void handleBulkDelete()}
                    disabled={bulkDeleting}
                  >
                    {bulkDeleting ? 'Deleting…' : 'Delete selected'}
                  </button>
                )}
              </div>

              {visibleAssets.length === 0 && <p>No files match the selected statuses.</p>}

              <ul className="assets-list">
                {shownAssets.map((asset) => {
                  const badge = processingBadge(asset, maxSourceChars)
                  const isOpen = expanded === asset.storedFileName

                  return (
                    <li className={isOpen ? 'asset asset-open' : 'asset'} key={asset.storedFileName}>
                      <input
                        type="checkbox"
                        className="asset-select"
                        checked={selected.has(asset.storedFileName)}
                        onChange={() => toggleOne(asset.storedFileName)}
                        disabled={bulkDeleting}
                        aria-label={`Select ${asset.originalFileName}`}
                      />

                      <button
                        type="button"
                        className="asset-head"
                        aria-expanded={isOpen}
                        onClick={() =>
                          setExpanded((current) =>
                            current === asset.storedFileName ? null : asset.storedFileName,
                          )
                        }
                      >
                        <span className="asset-name">{asset.originalFileName}</span>
                        <span className="asset-meta">
                          {formatSize(asset.sizeBytes)} · {formatDateTime(asset.uploadedAtUtc)}
                          {asset.capturedAtUtc && ` · Taken ${formatDateTime(asset.capturedAtUtc)}`}
                          {asset.latitude !== null && asset.longitude !== null &&
                            ` · ${formatCoordinates(asset.latitude, asset.longitude)}`}
                        </span>

                        {badge && (
                          <span className={`asset-status asset-status-${badge.tone}`}>{badge.text}</span>
                        )}
                      </button>

                      {isOpen && (
                        <div className="asset-detail">
                          {/* Key on the note id: a re-run makes a new note, and the panel should
                              reload rather than show the old body. */}
                          <FilePanel
                            key={`${asset.storedFileName}:${asset.noteId ?? ''}`}
                            asset={asset}
                            onChanged={reload}
                            onDeleted={removeRow}
                          />
                        </div>
                      )}
                    </li>
                  )
                })}
              </ul>

              {hasMore && (
                <button type="button" className="btn btn-md assets-show-more" onClick={showMore}>
                  Show {Math.min(10, visibleAssets.length - shownAssets.length)} more
                </button>
              )}
            </>
          )}
        </>
      )}
    </section>
  )
}

const TEXT_FILE = /\.(txt|md|markdown)$/i

type Badge = { text: string; tone: StatusTone }

/** Mirrors the server ProcessingStatus once a job exists; before that, a byte-size guess for
 *  text files - only ever a "possibly", the worker decides for real. Text/tone match the
 *  status toggles in the section header, so a row reads as the same status it's grouped
 *  under. */
function processingBadge(asset: AssetSummary, maxSourceChars: number): Badge | null {
  switch (asset.processingStatus) {
    case 'Pending':
      return { text: 'Pending', tone: 'pending' }
    case 'Running':
      return { text: 'Running', tone: 'running' }
    case 'Done':
      return asset.hasTags ? { text: 'Processed', tone: 'ok' } : { text: 'Not tagged', tone: 'warn' }
    case 'Failed':
      return { text: `Processing failed: ${asset.processingError ?? 'unknown error'}`, tone: 'error' }
    case 'Skipped':
      return { text: `Not processed: ${asset.processingError ?? 'source too large'}`, tone: 'warn' }
    default:
      return maxSourceChars > 0 &&
        TEXT_FILE.test(asset.originalFileName) &&
        asset.sizeBytes > maxSourceChars
        ? { text: 'Possibly too large to turn into a note', tone: 'warn' }
        : null
  }
}
