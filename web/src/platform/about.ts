import { useEffect, useState } from 'react'
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

/** The server's version for a signed-in person, `null` for nobody signed in, `undefined` while it loads or after it failed. */
export function useAboutVersion(): string | null | undefined {
  const [version, setVersion] = useState<string | null | undefined>(undefined)
  useEffect(() => {
    const controller = new AbortController()
    fetchAboutVersion(controller.signal).then(setVersion, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the server version failed', error)
      }
    })
    return () => controller.abort()
  }, [])
  return version
}
