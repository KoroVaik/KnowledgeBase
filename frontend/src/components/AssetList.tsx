import { useEffect, useState } from 'react'
import { assetDownloadUrl, deleteAsset, fetchAssets } from '../api/assets'
import type { AssetSummary } from '../api/assets'

type ListState =
  | { status: 'loading' }
  | { status: 'ready'; assets: AssetSummary[] }
  | { status: 'error'; message: string }

interface AssetListProps {
  /** Changing this reloads the list - the upload form bumps it after a successful upload. */
  reloadToken: number
}

export function AssetList({ reloadToken }: AssetListProps) {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [deletingFileName, setDeletingFileName] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)

  useEffect(() => {
    // A reload started earlier can finish later; the flag keeps its response from
    // overwriting a newer one.
    let cancelled = false

    // A reload deliberately leaves the current rows on screen instead of flipping back to
    // "Loading…" - the list would flash on every upload.
    void fetchAssets()
      .then((assets) => {
        if (!cancelled) {
          setState({ status: 'ready', assets })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setState({ status: 'error', message: messageOf(error) })
        }
      })

    return () => {
      cancelled = true
    }
  }, [reloadToken])

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
              <a className="asset-name" href={assetDownloadUrl(asset.storedFileName)}>
                {asset.storedFileName}
              </a>
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
