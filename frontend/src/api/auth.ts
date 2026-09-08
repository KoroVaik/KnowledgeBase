import { apiFetch, readErrorMessage } from './http'

export interface CurrentUser {
  name: string
}

/** Returns null when nobody is signed in, so callers do not have to inspect status codes. */
export async function fetchCurrentUser(): Promise<CurrentUser | null> {
  const response = await apiFetch('/api/auth/me')

  if (response.status === 401) {
    return null
  }

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Could not read the session'))
  }

  return (await response.json()) as CurrentUser
}

export async function login(password: string): Promise<CurrentUser> {
  const response = await apiFetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ password }),
  })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Sign in failed'))
  }

  return (await response.json()) as CurrentUser
}

export async function logout(): Promise<void> {
  const response = await apiFetch('/api/auth/logout', { method: 'POST' })

  if (!response.ok) {
    throw new Error(await readErrorMessage(response, 'Sign out failed'))
  }
}
