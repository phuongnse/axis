import { apiFetch } from './api'

/** The signed-in user, as served by `GET /api/me`. */
export interface CurrentUser {
  id: string
  displayName: string
  roles: string[]
  /** Whether the user may use Studio: it is on and the user holds the author role. */
  studio: boolean
  /** Whether the user may publish from Studio: the author and publisher roles, and Studio has folders. */
  studioPublisher: boolean
  /** Whether the user may create applications in Studio: the author role, and Studio has an applications folder. */
  studioCreate: boolean
  /** The host of the tenant's preview tenant, or `null` when the user may not use Studio or the tenant has none. */
  studioPreviewHost: string | null
  /** Whether the user sees the test controls in Studio: true for an author when the server's `Studio:Tests` is on. */
  studioTest: boolean
}

/**
 * Loads the signed-in user, or returns `null` when nobody is signed in. A 401 here does not start
 * sign-in, because the shell asks before anyone has chosen to sign in.
 */
export async function fetchCurrentUser(signal?: AbortSignal): Promise<CurrentUser | null> {
  const response = await apiFetch('/api/me', { signal }, { promptSignInOnUnauthorized: false })
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
