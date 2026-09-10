import { useCallback, useEffect, useRef, useState } from 'react'
import { deleteAsset, fetchAssets, fetchDownloadUrl, processAsset } from '../api/assets'
import type { AssetSummary } from '../api/assets'
import { formatSize } from '../format'
import { useResourceChanges } from '../hooks/useResourceChanges'

type ListState =
  | { status: 'loading' }
  | { status: 'ready'; assets: AssetSummary[] }
  | { status: 'error'; message: string }

interface AssetListProps {
  /** Bumped after a successful upload to reload the list - covers a down change stream. */
  reloadToken: number
  downloadEnabled: boolean
  /** From /api/features: threshold for the "possibly too large" hint on text files. */
  maxSourceChars: number
}

export function AssetList({ reloadToken, downloadEnabled, maxSourceChars }: AssetListProps) {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [deletingFileName, setDeletingFileName] = useState<string | null>(null)
  const [linkingFileName, setLinkingFileName] = useState<string | null>(null)
  const [processingFileName, setProcessingFileName] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

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
    state.assets.some((asset) => asset.processingStatus === 'Pending' || asset.processingStatus === 'Running')

  useEffect(() => {
    if (!hasActiveJob) {
      return
    }

    const timer = window.setInterval(reload, 4000)
    return () => window.clearInterval(timer)
  }, [hasActiveJob, reload])

  async function handleDownload(asset: AssetSummary) {
    setActionError(null)
    setLinkingFileName(asset.storedFileName)

    try {
      startDownload(await fetchDownloadUrl(asset.storedFileName))
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setLinkingFileName(null)
    }
  }

  async function handleProcess(asset: AssetSummary) {
    setActionError(null)
    setProcessingFileName(asset.storedFileName)

    try {
      await processAsset(asset.storedFileName)
      reload()
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setProcessingFileName(null)
    }
  }

  async function handleDelete(asset: AssetSummary) {
    // The note is a description of this file, so it goes too - say so before, not after.
    const warning =
      asset.noteId !== null
        ? `Delete ${asset.originalFileName}? Its note goes to the bin with it.`
        : `Delete ${asset.originalFileName}? This cannot be undone.`

    if (!window.confirm(warning)) {
      return
    }

    const fileName = asset.storedFileName

    setActionError(null)
    setDeletingFileName(fileName)

    try {
      await deleteAsset(fileName)
      setState((current) =>
        current.status === 'ready'
          ? {
              status: 'ready',
              assets: current.assets.filter((asset) => asset.storedFileName !== fileName),
            }
          : current,
      )
    } catch (error) {
      setActionError(messageOf(error))
    } finally {
      setDeletingFileName(null)
    }
  }

  return (
    <section className="assets">
      <h2>Uploaded files</h2>

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
        <ul className="assets-list">
          {state.assets.map((asset) => {
            const badge = processingBadge(asset, maxSourceChars)

            return (
            <li className="asset" key={asset.storedFileName}>
              {!downloadEnabled && <span className="asset-name">{asset.originalFileName}</span>}

              {/* A button, not a link: the signed URL does not exist until click and expires fast. */}
              {downloadEnabled && (
                <button
                  type="button"
                  className="asset-name asset-name-button"
                  onClick={() => void handleDownload(asset)}
                  disabled={linkingFileName === asset.storedFileName}
                >
                  {asset.originalFileName}
                </button>
              )}
              <span className="asset-meta">
                {formatSize(asset.sizeBytes)} · {new Date(asset.uploadedAtUtc).toLocaleString()}
              </span>
              {/* Both buttons in one grid cell so the status badge keeps its own line. */}
              <div className="asset-actions">
                {canProcess(asset) && (
                  <button
                    type="button"
                    className="asset-delete"
                    onClick={() => void handleProcess(asset)}
                    disabled={processingFileName === asset.storedFileName}
                    title="Run the pipeline over this file and make a note from it"
                  >
                    {processingFileName === asset.storedFileName ? 'Queueing…' : 'Process'}
                  </button>
                )}
                <button
                  type="button"
                  className="asset-delete"
                  onClick={() => void handleDelete(asset)}
                  disabled={deletingFileName === asset.storedFileName}
                >
                  {deletingFileName === asset.storedFileName ? 'Deleting…' : 'Delete'}
                </button>
              </div>

              {badge && (
                <span className={`asset-status asset-status-${badge.tone}`}>{badge.text}</span>
              )}
            </li>
            )
          })}
        </ul>
      )}
    </section>
  )
}

/** A hidden frame, not window.location: an attachment downloads either way, but a bucket XML
 *  error would replace the whole app under top-level navigation. In a frame it is discarded. */
function startDownload(url: string) {
  const frame = document.createElement('iframe')
  frame.hidden = true
  frame.src = url
  document.body.appendChild(frame)

  // The download outlives the frame once headers are seen; this only covers the round trip.
  window.setTimeout(() => frame.remove(), 60_000)
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}

/** File with no note and no pending job. Not for 'Skipped' - a rerun cannot change the fit. */
function canProcess(asset: AssetSummary): boolean {
  return (
    asset.noteId === null &&
    (asset.processingStatus === null || asset.processingStatus === 'Failed')
  )
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
