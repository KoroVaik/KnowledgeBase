import { useSyncExternalStore } from 'react'
import { getPreference, setPreference, subscribePreferences } from './preferences'

/** One saved UI setting, `fallback` until the user has changed it. */
export function usePreference<T>(key: string, fallback: T, isValid: (value: unknown) => value is T) {
  const stored = useSyncExternalStore(subscribePreferences, () => getPreference(key))
  const value = isValid(stored) ? stored : fallback

  function update(next: T) {
    setPreference(key, next)
  }

  return [value, update] as const
}
