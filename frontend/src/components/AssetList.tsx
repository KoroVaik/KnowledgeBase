import { useCallback, useEffect, useRef, useState } from 'react'
import { deleteAsset, fetchAssets } from '../api/assets'
import type { AssetSummary } from '../api/assets'
import { formatDateTime, formatSize } from '../format'
import { useResourceChanges } from '../hooks/useResourceChanges'
import { FilePanel } from './FilePanel'

type ListState =
  | { status: 'loading' }
  | { status: 'ready'; assets: AssetSummary[] }
  | { status: 'error'; message: string }

interface AssetListProps {
  /** Bumped after a successful upload to reload the list - covers a down change stream. */
  reloadToken: number
  /** From /api/features: threshold for the "possibly too large" hint on text files. */
  maxSourceChars: number
}

export function AssetList({ reloadToken, maxSourceChars }: AssetListProps) {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [expanded, setExpanded] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  // Stray names (a file selected then removed via SSE) stay in the set but are ignored on
  // render and cleared on the next delete - cheaper than pruning in an effect, which oxlint
  // react(set-state-in-effect) would flag anyway.
  const [selected, setSelected] = useState<ReadonlySet<string>>(new Set())
  const [bulkDeleting, setBulkDeleting] = useState(false)

  // The change stream fires reloads too, so two can be in flight - only the newest writes state.
  const latestReload = useRef(0)

  const reload = useCallback(() => {
    const reloadId = ++latestReload.current

    // Leave the current rows on screen, don't flash "Loading…" on every upload.
    void fetchAssets()
      .then((assets) => {
        if (reloadId === latestReload.current) {
          setState({ status: 'ready', assets })
        }
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

  useEffect(reload, [reload, reloadToken])

  useResourceChanges('assets', reload)

  // The worker has no change stream back to the browser, so poll while a job is in flight.
  const hasActiveJob =
    state.status === 'ready' &&
    state.assets.some(
      (asset) => asset.processingStatus === 'Pending' || asset.processingStatus === 'Running',
    )

  useEffect(() => {
    if (!hasActiveJob) {
      return
    }

    const timer = window.setInterval(reload, 4000)
    return () => window.clearInterval(timer)
  }, [hasActiveJob, reload])

  const assets = state.status === 'ready' ? state.assets : []
  const selectedAssets = assets.filter((asset) => selected.has(asset.storedFileName))
  const allSelected = assets.length > 0 && selectedAssets.length === assets.length

  function removeRow(storedFileName: string) {
    setState((current) =>
      current.status === 'ready'
        ? {
            status: 'ready',
            assets: current.assets.filter((asset) => asset.storedFileName !== storedFileName),
          }
        : current,
    )
    setExpanded((current) => (current === storedFileName ? null : current))
  }

  function toggleOne(fileName: string) {
    setSelected((current) => {
      const next = new Set(current)
      if (!next.delete(fileName)) {
        next.add(fileName)
      }
      return next
    })
  }

  function toggleAll() {
    setSelected(allSelected ? new Set() : new Set(assets.map((asset) => asset.storedFileName)))
  }

  async function handleBulkDelete() {
    const targets = selectedAssets
    if (targets.length === 0) {
      return
    }

    const withNote = targets.filter((asset) => asset.noteId !== null).length
    const tail =
      withNote > 0
        ? `${withNote} of them ${withNote === 1 ? 'has a note that goes' : 'have notes that go'} to the bin too.`
        : 'This cannot be undone.'

    if (!window.confirm(`Delete ${targets.length} ${targets.length === 1 ? 'file' : 'files'}? ${tail}`)) {
      return
    }

    setActionError(null)
    setBulkDeleting(true)

    const results = await Promise.allSettled(targets.map((asset) => deleteAsset(asset.storedFileName)))
    const failed = new Set<string>()
    results.forEach((result, index) => {
      if (result.status === 'rejected') {
        failed.add(targets[index].storedFileName)
      }
    })

    setState((current) =>
      current.status === 'ready'
        ? {
            status: 'ready',
            assets: current.assets.filter(
              (asset) => !selected.has(asset.storedFileName) || failed.has(asset.storedFileName),
            ),
          }
        : current,
    )
    setSelected(failed)
    setBulkDeleting(false)

    if (failed.size > 0) {
      setActionError(`Could not delete ${failed.size} of ${targets.length} files.`)
    }
  }

  return (
    <section className="assets">
      <h2>
        Files
        {state.status === 'ready' && <span className="section-count">{state.assets.length}</span>}
      </h2>

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
                className="asset-delete"
                onClick={() => void handleBulkDelete()}
                disabled={bulkDeleting}
              >
                {bulkDeleting ? 'Deleting…' : 'Delete selected'}
              </button>
            )}
          </div>

          <ul className="assets-list">
            {state.assets.map((asset) => {
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
                    </span>
                  </button>

                  {badge && (
                    <span className={`asset-status asset-status-${badge.tone}`}>{badge.text}</span>
                  )}

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
        </>
      )}
    </section>
  )
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}

const TEXT_FILE = /\.(txt|md|markdown)$/i

type Badge = { text: string; tone: 'info' | 'ok' | 'warn' | 'error' }

/** Mirrors the server ProcessingStatus once a job exists; before that, a byte-size guess for
 *  text files - only ever a "possibly", the worker decides for real. */
function processingBadge(asset: AssetSummary, maxSourceChars: number): Badge | null {
  switch (asset.processingStatus) {
    case 'Pending':
    case 'Running':
      return { text: 'Processing…', tone: 'info' }
    case 'Done':
      return { text: 'Note ready', tone: 'ok' }
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
