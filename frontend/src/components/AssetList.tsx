import { useCallback, useEffect, useRef, useState } from 'react'
import { deleteAsset, fetchAssets, fetchDownloadUrl } from '../api/assets'
import type { AssetSummary } from '../api/assets'
import { formatSize } from '../format'
import { useResourceChanges } from '../hooks/useResourceChanges'

type ListState =
  | { status: 'loading' }
  | { status: 'ready'; assets: AssetSummary[] }
  | { status: 'error'; message: string }

interface AssetListProps {
  /**
   * Changing this reloads the list - the upload form bumps it after a successful upload.
   * Kept even though the server announces that upload over the change stream too: it shows
   * the new row without waiting for the round trip, and it still works if the stream is down.
   */
  reloadToken: number
  downloadEnabled: boolean
  /** From /api/features: threshold for the "possibly too large" hint on text files. */
  maxSourceChars: number
}

export function AssetList({ reloadToken, downloadEnabled, maxSourceChars }: AssetListProps) {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [deletingFileName, setDeletingFileName] = useState<string | null>(null)
  const [linkingFileName, setLinkingFileName] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  // Reloads are no longer triggered by this component alone - the change stream fires them
  // too, and two can be in flight at once. Only the newest one may write to the state.
  const latestReload = useRef(0)

  const reload = useCallback(() => {
    const reloadId = ++latestReload.current

    // A reload deliberately leaves the current rows on screen instead of flipping back to
    // "Loading…" - the list would flash on every upload.
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

        // A reload nobody asked for is allowed to fail quietly: rows already on screen are
        // still the best answer available, and blanking them on a flaky connection would be
        // worse than showing them a minute stale.
        setState((current) =>
          current.status === 'ready' ? current : { status: 'error', message: messageOf(error) },
        )
      })
  }, [])

  useEffect(reload, [reload, reloadToken])

  useResourceChanges('assets', reload)

  // The worker runs in its own process with no change stream back to the browser, so a job
  // finishing is not announced. While anything is still in flight, poll for the status flip;
  // stop once every row has reached a terminal state.
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

  async function handleDelete(asset: AssetSummary) {
    if (!window.confirm(`Delete ${asset.originalFileName}? This cannot be undone.`)) {
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

              {/* A button, not a link: there is no URL to put in href until the API signs
                  one, and it would be stale by the time anyone clicked it. */}
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
              <button
                type="button"
                className="asset-delete"
                onClick={() => void handleDelete(asset)}
                disabled={deletingFileName === asset.storedFileName}
              >
                {deletingFileName === asset.storedFileName ? 'Deleting…' : 'Delete'}
              </button>

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

/**
 * A hidden frame rather than window.location: an attachment response downloads either way,
 * but anything else - an expired link, an object the bucket no longer has - is an XML error
 * page, and top-level navigation would replace the whole app with it. In a frame that answer
 * is simply discarded.
 */
function startDownload(url: string) {
  const frame = document.createElement('iframe')
  frame.hidden = true
  frame.src = url
  document.body.appendChild(frame)

  // The download outlives the frame once the browser has seen the headers; the delay only has
  // to cover the round trip to the bucket.
  window.setTimeout(() => frame.remove(), 60_000)
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}

const TEXT_FILE = /\.(txt|md|markdown)$/i

type Badge = { text: string; tone: 'info' | 'ok' | 'warn' | 'error' }

/**
 * The processing state to show next to a file. Mirrors the server-side ProcessingStatus once
 * a job exists; before that, a size-based guess for text files the pipeline will likely skip.
 * Byte size, not characters, so it is only ever a "possibly" - the worker decides for real.
 */
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
