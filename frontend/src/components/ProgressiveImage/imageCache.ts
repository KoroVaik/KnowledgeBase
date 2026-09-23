import { startRequest } from '../../diagnostics/diagnostics'
import { fetchDownloadUrl } from '../../api/assets'
import type { ProgressiveImageState } from './useProgressiveImage'

export const emptyImage: ProgressiveImageState = { percent: 0, imageUrl: null, failed: false }
type Entry = { key: string; url: string; state: ProgressiveImageState; listeners: Set<() => void>; bytes: number; used: number; request?: XMLHttpRequest; queued: boolean; retried: boolean }
const entries = new Map<string, Entry>()
const queue: Entry[] = []
let active = 0
let cleanupTimer: ReturnType<typeof setTimeout> | undefined

function keyOf(url: string) { return url.split(/[?#]/, 1)[0] }

function publish(entry: Entry, state: ProgressiveImageState) {
  entry.state = state
  for (const listener of entry.listeners) listener()
}

function remove(entry: Entry) {
  entries.delete(entry.key)
  entry.queued = false
  if (entry.request && entry.request.readyState !== XMLHttpRequest.DONE) entry.request.abort()
  if (entry.state.imageUrl) URL.revokeObjectURL(entry.state.imageUrl)
  publish(entry, emptyImage)
}

function trim() {
  const idle = [...entries.values()].filter(entry => entry.listeners.size === 0).sort((a, b) => a.used - b.used)
  let bytes = idle.reduce((sum, entry) => sum + entry.bytes, 0)
  let count = idle.length
  for (const entry of idle) {
    if (entry.request || entry.queued || entry.state.failed || count > 64 || bytes > 64 * 1024 * 1024) {
      bytes -= entry.bytes; count--; remove(entry)
    }
  }
}

function pump() {
  while (active < 4 && queue.length) {
    const entry = queue.shift()!
    if (!entry.queued || entries.get(entry.key) !== entry) continue
    entry.queued = false
    if (!entry.listeners.size) continue
    download(entry)
  }
}

function download(entry: Entry) {
  active++
  const request = new XMLHttpRequest()
  entry.request = request
  const fileName = decodeURIComponent(entry.key.substring(entry.key.lastIndexOf('/') + 1))
  const context = { source: 'ProgressiveImage', trigger: 'url-change', assetId: fileName.split('.')[0] }
  const diagnostic = startRequest('GET', 'bucket/object', context)
  request.open('GET', entry.url)
  request.responseType = 'blob'
  request.timeout = 120_000
  request.addEventListener('progress', event => {
    if (event.lengthComputable) publish(entry, { ...emptyImage, percent: event.loaded / event.total })
  })
  const fail = () => publish(entry, { ...emptyImage, failed: true })
  request.addEventListener('error', fail)
  request.addEventListener('timeout', fail)
  request.addEventListener('load', () => {
    if (entries.get(entry.key) !== entry) return
    if ((request.status === 401 || request.status === 403) && !entry.retried) {
      entry.retried = true
      void fetchDownloadUrl(fileName, context, true).then(url => {
        if (entries.get(entry.key) !== entry) return
        if (!entry.listeners.size) { remove(entry); return }
        entry.url = url; entry.queued = true; queue.push(entry); pump()
      }).catch(() => { if (entries.get(entry.key) === entry) fail() })
      return
    }
    if (request.status < 200 || request.status > 299 || !(request.response instanceof Blob)) { fail(); return }
    entry.bytes = request.response.size
    publish(entry, { percent: 1, imageUrl: URL.createObjectURL(request.response), failed: false })
    trim()
  })
  request.addEventListener('loadend', () => {
    diagnostic.finish(request.status, request.status === 0 ? 'network-error' : 'completed', entry.bytes)
    entry.request = undefined
    active--; pump()
  })
  request.send()
}

export function imageSnapshot(url: string | null): ProgressiveImageState {
  return url === null ? emptyImage : entries.get(keyOf(url))?.state ?? emptyImage
}

export function subscribeImage(url: string | null, listener: () => void) {
  if (url === null) return () => {}
  const key = keyOf(url)
  let entry = entries.get(key)
  if (!entry) {
    entry = { key, url, state: emptyImage, listeners: new Set(), bytes: 0, used: Date.now(), queued: false, retried: false }
    entries.set(key, entry)
  }
  const target = entry
  target.listeners.add(listener)
  if (target.state === emptyImage && !target.request && !target.queued) {
    target.queued = true; queue.push(target); pump()
  }
  return () => {
    target.listeners.delete(listener)
    target.used = Date.now()
    if (cleanupTimer === undefined) cleanupTimer = setTimeout(() => { cleanupTimer = undefined; trim() }, 1000)
  }
}

export function clearImages(fileName?: string) {
  for (const entry of [...entries.values()]) {
    if (fileName === undefined || entry.key.endsWith(`/${encodeURIComponent(fileName)}`) || entry.key.endsWith(`/${fileName}`)) remove(entry)
  }
}
