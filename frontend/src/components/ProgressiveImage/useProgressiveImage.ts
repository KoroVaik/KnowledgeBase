import { useEffect, useState } from 'react'

export interface ProgressiveImageState {
  /** Bytes received over total, 0–1. */
  percent: number
  /** Object URL of the fully downloaded image, ready for <img>. */
  imageUrl: string | null
  failed: boolean
}

// XHR, not fetch: only xhr reports download progress (the same reason the upload path uses
// it for request progress). A plain <img> gives the browser the bytes invisibly, so there
// would be nothing to show as a percent.
export function useProgressiveImage(url: string | null): ProgressiveImageState {
  const [state, setState] = useState<ProgressiveImageState & { source: string | null }>({
    source: url,
    percent: 0,
    imageUrl: null,
    failed: false,
  })

  // Reset during render when the url changes - a synchronous setState inside the effect
  // trips oxlint react(set-state-in-effect), same reason the other fetch effects seed state.
  if (state.source !== url) {
    setState({ source: url, percent: 0, imageUrl: null, failed: false })
  }

  useEffect(() => {
    if (url === null) {
      return
    }

    let cancelled = false
    let objectUrl: string | null = null
    const request = new XMLHttpRequest()

    request.open('GET', url)
    request.responseType = 'blob'

    request.addEventListener('progress', (event) => {
      if (cancelled || !event.lengthComputable) {
        return
      }
      setState({ source: url, percent: event.loaded / event.total, imageUrl: null, failed: false })
    })

    // A blocked cross-origin GET (bucket CORS) and a dead network both leave status 0.
    request.addEventListener('error', () => {
      if (!cancelled) {
        setState({ source: url, percent: 0, imageUrl: null, failed: true })
      }
    })

    request.addEventListener('load', () => {
      if (cancelled) {
        return
      }
      const blob = request.response
      if (request.status < 200 || request.status > 299 || !(blob instanceof Blob)) {
        setState({ source: url, percent: 0, imageUrl: null, failed: true })
        return
      }
      objectUrl = URL.createObjectURL(blob)
      setState({ source: url, percent: 1, imageUrl: objectUrl, failed: false })
    })

    request.send()

    return () => {
      cancelled = true
      request.abort()
      if (objectUrl !== null) {
        URL.revokeObjectURL(objectUrl)
      }
    }
  }, [url])

  const { percent, imageUrl, failed } = state
  return { percent, imageUrl, failed }
}
