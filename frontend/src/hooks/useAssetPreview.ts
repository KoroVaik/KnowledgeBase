import { useEffect, useState } from 'react'
import { fetchDownloadUrl } from '../api/assets'

export function useAssetPreview(fileName: string | undefined, assetId: string | undefined, source: string) {
  const [state, setState] = useState<{ fileName?: string; url: string | null; unavailable: boolean }>({ fileName, url: null, unavailable: false })
  if (state.fileName !== fileName) setState({ fileName, url: null, unavailable: false })
  useEffect(() => {
    if (fileName === undefined) return
    let cancelled = false
    void fetchDownloadUrl(fileName, { source, trigger: 'asset-change', assetId })
      .then(url => { if (!cancelled) setState({ fileName, url, unavailable: false }) })
      .catch(() => { if (!cancelled) setState({ fileName, url: null, unavailable: true }) })
    return () => { cancelled = true }
  }, [fileName, assetId, source])
  return state
}
