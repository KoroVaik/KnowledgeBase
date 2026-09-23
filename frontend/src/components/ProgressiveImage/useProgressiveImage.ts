import { useCallback, useSyncExternalStore } from 'react'
import { emptyImage, imageSnapshot, subscribeImage } from './imageCache'

export interface ProgressiveImageState {
  /** Bytes received over total, 0–1. */
  percent: number
  /** Object URL of the fully downloaded image, ready for <img>. */
  imageUrl: string | null
  failed: boolean
}

export function useProgressiveImage(url: string | null): ProgressiveImageState {
  const subscribe = useCallback((listener: () => void) => subscribeImage(url, listener), [url])
  const snapshot = useCallback(() => imageSnapshot(url), [url])
  return useSyncExternalStore(subscribe, snapshot, () => emptyImage)
}
