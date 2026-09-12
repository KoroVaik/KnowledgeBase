import { useCallback, useEffect, useRef, useState } from 'react'
import { deleteAsset, fetchAssets } from '../../api/assets'
import type { AssetSummary } from '../../api/assets'
import { useResourceChanges } from '../../hooks/useResourceChanges'

export type ListState =
  | { status: 'loading' }
  | { status: 'ready'; assets: AssetSummary[] }
  | { status: 'error'; message: string }

/** 'None' stands for `processingStatus === null` - not a pipeline type, or no job yet. */
export type StatusValue = 'Pending' | 'Running' | 'Done' | 'Failed' | 'Skipped' | 'None'

export type StatusCounts = Record<StatusValue, number>

const ALL_STATUS_VALUES: StatusValue[] = ['Pending', 'Running', 'Done', 'Failed', 'Skipped', 'None']

function statusValueOf(asset: AssetSummary): StatusValue {
  return (asset.processingStatus as StatusValue | null) ?? 'None'
}

function matchesFilter(asset: AssetSummary, selected: ReadonlySet<StatusValue>): boolean {
  return selected.has(statusValueOf(asset))
}

function countByStatus(assets: AssetSummary[]): StatusCounts {
  const counts: StatusCounts = { Pending: 0, Running: 0, Done: 0, Failed: 0, Skipped: 0, None: 0 }
  for (const asset of assets) {
    switch (asset.processingStatus) {
      case 'Pending':
        counts.Pending += 1
        break
      case 'Running':
        counts.Running += 1
        break
      case 'Done':
        counts.Done += 1
        break
      case 'Failed':
        counts.Failed += 1
        break
      case 'Skipped':
        counts.Skipped += 1
        break
      default:
        counts.None += 1
        break
    }
  }
  return counts
}

/** State and actions behind AssetList: load/poll while a job is running, expand/select rows,
 *  filter by status, and bulk delete. `reloadToken` is bumped by the parent after a successful
 *  upload. */
const PAGE_SIZE = 10

export function useAssetList(reloadToken: number) {
  const [state, setState] = useState<ListState>({ status: 'loading' })
  const [expanded, setExpanded] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [selectedStatuses, setSelectedStatuses] = useState<ReadonlySet<StatusValue>>(
    new Set(ALL_STATUS_VALUES),
  )
  const [visibleLimit, setVisibleLimit] = useState(PAGE_SIZE)

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
  const statusCounts = countByStatus(assets)
  const visibleAssets = assets.filter((asset) => matchesFilter(asset, selectedStatuses))
  // Only the newest PAGE_SIZE (per filter) are rendered - "Show more" raises the limit.
  const shownAssets = visibleAssets.slice(0, visibleLimit)
  const hasMore = visibleAssets.length > shownAssets.length

  // Selection, "select all", and bulk delete only ever touch rows actually on screen -
  // a row hidden by the filter or past the page limit keeps its checked state but doesn't
  // count or get deleted.
  const selectedAssets = shownAssets.filter((asset) => selected.has(asset.storedFileName))
  const allSelected = shownAssets.length > 0 && selectedAssets.length === shownAssets.length

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

  function toggleStatus(value: StatusValue) {
    setSelectedStatuses((current) => {
      const next = new Set(current)
      if (!next.delete(value)) {
        next.add(value)
      }
      return next
    })
    setVisibleLimit(PAGE_SIZE)
  }

  function showMore() {
    setVisibleLimit((current) => current + PAGE_SIZE)
  }

  function toggleAll() {
    setSelected((current) => {
      const next = new Set(current)
      for (const asset of shownAssets) {
        if (allSelected) {
          next.delete(asset.storedFileName)
        } else {
          next.add(asset.storedFileName)
        }
      }
      return next
    })
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

  return {
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
  }
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Unexpected error'
}
