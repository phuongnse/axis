import { useEffect, useState } from 'react'

export type ServerStatus = 'checking' | 'ready' | 'unavailable'

/** Reads the server readiness endpoint once, when the component mounts. */
export function useServerStatus(): ServerStatus {
  const [status, setStatus] = useState<ServerStatus>('checking')

  useEffect(() => {
    const controller = new AbortController()
    fetch('/health/ready', { signal: controller.signal })
      .then((response) => setStatus(response.ok ? 'ready' : 'unavailable'))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Readiness check failed', error)
          setStatus('unavailable')
        }
      })
    return () => controller.abort()
  }, [])

  return status
}
