import { useEffect, useState } from 'react'
import { apiFetch } from './api'

/**
 * Proposed: the Axis version, as `GET /api/about` serves it to a signed-in user, the server's informational version
 * that the images carry. It is `null` with nobody signed in, so the version never shows to an anonymous visitor.
 */
export function useAxisVersion(): string | null {
  const [version, setVersion] = useState<string | null>(null)
  useEffect(() => {
    const controller = new AbortController()
    apiFetch('/api/about', { signal: controller.signal }, { promptSignInOnUnauthorized: false })
      .then(async (response) => {
        if (response.ok) {
          setVersion(((await response.json()) as { version: string }).version)
        }
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the Axis version failed', error)
        }
      })
    return () => controller.abort()
  }, [])
  return version
}
