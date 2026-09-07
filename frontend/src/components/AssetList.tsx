import { useCallback, useEffect, useRef, useState } from 'react'
import { assetDownloadUrl, deleteAsset, fetchAssets } from '../api/assets'
import type { AssetSummary } from '../api/assets'
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
}

export function AssetList({ reloadToken, downloadEnabled }: AssetListProps) {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [deletingFileName, setDeletingFileName] = useState<string | null>(null)
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

  async function handleDelete(fileName: string) {
    if (!window.confirm(`Delete ${fileName}? This cannot be undone.`)) {
      return
    }

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
          {state.assets.map((asset) => (
            <li className="asset" key={asset.storedFileName}>
              {downloadEnabled ? (
                <a className="asset-name" href={assetDownloadUrl(asset.storedFileName)}>
                  {asset.storedFileName}
                </a>
              ) : (
                <span className="asset-name">{asset.storedFileName}</span>
              )}
              <span className="asset-meta">
                {formatSize(asset.sizeBytes)} · {new Date(asset.lastModifiedUtc).toLocaleString()}
              </span>
              <button
                type="button"
                className="asset-delete"
                onClick={() => void handleDelete(asset.storedFileName)}
                disabled={deletingFileName === asset.storedFileName}
              >
                {deletingFileName === asset.storedFileName ? 'Deleting…' : 'Delete'}
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function formatSize(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }

  if (bytes < 1024 * 1024) {
    return `${(bytes / 1024).toFixed(1)} KB`
  }

  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
