import { apiFetch } from './api'

/**
 * Loads the server's version from `GET /api/about`, or returns `null` when nobody is signed in. A 401
 * here does not start sign-in, because the footer asks before anyone has chosen to sign in.
 */
export async function fetchAboutVersion(signal?: AbortSignal): Promise<string | null> {
  const response = await apiFetch('/api/about', { signal }, { promptSignInOnUnauthorized: false })
  if (response.status === 401) {
    return null
  }
  if (!response.ok) {
    throw new Error(`Loading the server version failed with status ${response.status}.`)
  }
  return ((await response.json()) as { version: string }).version
}
