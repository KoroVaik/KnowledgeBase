import { useCallback, useEffect, useRef, useState } from 'react'
import { fetchAssets } from '../api/assets'
import type { AssetSummary } from '../api/assets'
import { useResourceChanges } from './useResourceChanges'

export function useVisibleAssets(enabled: boolean) {
  const [assets, setAssets] = useState<AssetSummary[]>([])
  const revision = useRef(0)
  const reload = useCallback(() => {
    if (!enabled) return
    const request = ++revision.current
    void fetchAssets().then(next => {
      if (request === revision.current) setAssets(next)
    }).catch(() => { /* The comparison remains readable without its preview. */ })
  }, [enabled])
  const invalidate = useCallback(() => { revision.current++ }, [])
  useEffect(() => { reload(); return invalidate }, [reload, invalidate])
  useResourceChanges('assets', reload)
  return assets
}
