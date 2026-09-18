import { apiFetch, readErrorMessage } from './http'

export type Preferences = Record<string, unknown>

/** Every UI setting the signed-in user has saved, key → JSON value. */
export async function fetchPreferences(): Promise<Preferences> {
  const response = await apiFetch('/api/preferences')

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not load the settings'))
  }

  return (await response.json()) as Preferences
}

export async function savePreference(key: string, value: unknown): Promise<void> {
  const response = await apiFetch(`/api/preferences/${encodeURIComponent(key)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(value),
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not save the setting'))
  }
}
