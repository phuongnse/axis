import { useEffect, useState } from 'react'
import { fetchCurrentUser, type CurrentUser } from './currentUser'
import { fetchSites, type SiteListItem } from './site'

/** A title in the current locale, or in its first locale when it lacks the current one. */
export function localizedTitle(titles: Record<string, string>, locale: string, fallback: string): string {
  return titles[locale] ?? Object.values(titles)[0] ?? fallback
}

/** The signed-in user: `undefined` while it loads or when loading fails, `null` when nobody is signed in. */
export function useCurrentUser(): CurrentUser | null | undefined {
  const [user, setUser] = useState<CurrentUser | null | undefined>(undefined)
  useEffect(() => {
    const controller = new AbortController()
    fetchCurrentUser(controller.signal).then(setUser, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the current user failed', error)
      }
    })
    return () => controller.abort()
  }, [])
  return user
}

/** The tenant's sites: `undefined` while they load, `null` when nobody is signed in, and `failed` when loading failed. */
export function useSites(): { sites: SiteListItem[] | null | undefined; failed: boolean } {
  const [sites, setSites] = useState<SiteListItem[] | null>()
  const [failed, setFailed] = useState(false)
  useEffect(() => {
    const controller = new AbortController()
    fetchSites(controller.signal)
      .then(setSites)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the sites failed', error)
          setFailed(true)
        }
      })
    return () => controller.abort()
  }, [])
  return { sites, failed }
}
