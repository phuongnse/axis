import { apiFetch } from './api'

/** The signed-in user, as served by `GET /api/me`. */
export interface CurrentUser {
  id: string
  displayName: string
  roles: string[]
}

/**
 * Loads the signed-in user, or returns `null` when nobody is signed in. A 401 here does not start
 * sign-in, because the shell asks before anyone has chosen to sign in.
 */
export async function fetchCurrentUser(signal?: AbortSignal): Promise<CurrentUser | null> {
  const response = await apiFetch('/api/me', { signal }, { signInOnUnauthorized: false })
  if (response.status === 401) {
    return null
  }
  if (!response.ok) {
    throw new Error(`Loading the current user failed with status ${response.status}.`)
  }
  return (await response.json()) as CurrentUser
}

/** Ends the session and returns the identity provider's sign-out address, for the browser to go to. */
export async function signOutSession(): Promise<string> {
  const response = await apiFetch('/api/auth/sign-out', { method: 'POST' })
  if (!response.ok) {
    throw new Error(`Signing out failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { signOutUrl: string }
  return body.signOutUrl
}
