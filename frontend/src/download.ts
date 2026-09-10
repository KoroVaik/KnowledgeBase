/** A hidden frame, not window.location: an attachment downloads either way, but a bucket XML
 *  error (stale link, missing object) would replace the whole app under top-level navigation.
 *  In a frame it is discarded. */
export function startDownload(url: string) {
  const frame = document.createElement('iframe')
  frame.hidden = true
  frame.src = url
  document.body.appendChild(frame)

  // The download outlives the frame once headers are seen; this only covers the round trip.
  window.setTimeout(() => frame.remove(), 60_000)
}
