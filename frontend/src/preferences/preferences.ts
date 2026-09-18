import { fetchPreferences, savePreference } from '../api/preferences'
import type { Preferences } from '../api/preferences'

const CACHE_PREFIX = 'kb.preferences.'
const LEGACY_COLLAPSED_PREFIX = 'kb.section-collapsed.'

interface Cache {
  values: Preferences
  /** Changed here but not yet acknowledged by the server - re-sent on the next start. */
  pending: Preferences
}

let userId: string | null = null
let values: Preferences = {}
let pending: Preferences = {}
const listeners = new Set<() => void>()

/** Must run before the signed-in page renders: the per-user cache is read synchronously so
 *  sections come up in their saved state instead of flipping once the server answers. */
export function startPreferences(id: string) {
  if (id === userId) {
    return
  }

  userId = id
  const cache = readCache(id)
  pending = { ...takeLegacyCollapsed(), ...cache.pending }
  values = { ...cache.values, ...pending }
  writeCache()
  notify()

  void fetchPreferences()
    .then((server) => {
      if (userId !== id) {
        return
      }

      values = { ...server, ...pending }
      writeCache()
      notify()
      Object.keys(pending).forEach(flush)
    })
    .catch(() => {
      // Offline or a dead API: the cached values stay on screen, pending ones wait for next start.
    })
}

export function stopPreferences() {
  userId = null
  values = {}
  pending = {}
  notify()
}

export function getPreference(key: string): unknown {
  return values[key]
}

export function setPreference(key: string, value: unknown) {
  values = { ...values, [key]: value }
  pending = { ...pending, [key]: value }
  writeCache()
  notify()
  flush(key)
}

export function subscribePreferences(listener: () => void) {
  listeners.add(listener)

  return () => {
    listeners.delete(listener)
  }
}

function flush(key: string) {
  const id = userId
  const value = pending[key]

  void savePreference(key, value)
    .then(() => {
      // A newer toggle may have landed while this one was in flight - keep that one pending.
      if (userId === id && pending[key] === value) {
        const { [key]: _saved, ...rest } = pending
        pending = rest
        writeCache()
      }
    })
    .catch(() => {
      // Stays pending in the cache and is re-sent on the next start.
    })
}

function notify() {
  listeners.forEach((listener) => listener())
}

function readCache(id: string): Cache {
  try {
    const raw = localStorage.getItem(CACHE_PREFIX + id)
    if (raw !== null) {
      const parsed = JSON.parse(raw) as Partial<Cache>
      return { values: parsed.values ?? {}, pending: parsed.pending ?? {} }
    }
  } catch {
    // Storage denied or a corrupt entry - start empty, the server fills it in.
  }

  return { values: {}, pending: {} }
}

function writeCache() {
  if (userId === null) {
    return
  }

  try {
    localStorage.setItem(CACHE_PREFIX + userId, JSON.stringify({ values, pending }))
  } catch {
    // Private browsing / storage denial - the server copy still holds the setting.
  }
}

/** Reads and removes the per-browser collapsed flags the app kept before the server store;
 *  they go up as pending writes, so the first sign-in after the move keeps the old layout. */
function takeLegacyCollapsed(): Preferences {
  const legacy: Preferences = {}

  try {
    for (const storageKey of Object.keys(localStorage)) {
      if (storageKey.startsWith(LEGACY_COLLAPSED_PREFIX)) {
        const section = storageKey.slice(LEGACY_COLLAPSED_PREFIX.length)
        legacy[`section-collapsed:${section}`] = localStorage.getItem(storageKey) === '1'
        localStorage.removeItem(storageKey)
      }
    }
  } catch {
    // Storage denied - nothing to migrate.
  }

  return legacy
}
