import { useEffect, useMemo, useState } from 'react'
import { ShellError } from './pages/LocaleLoadFailed'
import { PlatformContext, type Platform, type PlatformTexts } from './pages/context'
import { PlatformRoutes } from './pages/PlatformRoutes'
import { readStoredLocale } from './platform/locale'
import { SignInPrompt } from './platform/SignInPrompt'
import { TextProvider } from './platform/TextProvider'
import { fetchSite, type SiteMetadata } from './platform/site'
import { setHostSite } from './platform/siteLinks'
import { fetchTexts, type TextMap } from './platform/texts'
import { LeaveWithoutSaving, UnsavedChangesGuard } from './platform/UnsavedChangesGuard'

interface LoadedPlatform {
  site: SiteMetadata
  fallbackTexts: TextMap
  current: PlatformTexts
}

/** Loads the platform site and its texts once, then renders the matching route. */
export function PlatformRoot({ development }: { development: boolean }) {
  const [loaded, setLoaded] = useState<LoadedPlatform>()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      const site = await fetchSite(controller.signal)
      setHostSite(site.hostSite ?? null)
      const locale = readStoredLocale(site.locales.available, site.locales.default)
      const [fallbackTexts, texts] = await Promise.all([
        fetchTexts(site.locales.fallback, controller.signal),
        locale === site.locales.fallback ? undefined : fetchTexts(locale, controller.signal),
      ])
      setLoaded({ site, fallbackTexts, current: { locale, texts: texts ?? fallbackTexts } })
    }
    load().catch((error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the site failed', error)
        setFailed(true)
      }
    })
    return () => controller.abort()
  }, [])

  const platform = useMemo<Platform | undefined>(
    () =>
      loaded && {
        ...loaded,
        development,
        setCurrent: (current) => setLoaded((previous) => previous && { ...previous, current }),
      },
    [loaded, development],
  )

  const catalogs = useMemo(
    () => loaded && [{ texts: loaded.current.texts, fallbackTexts: loaded.fallbackTexts }],
    [loaded],
  )

  if (failed) {
    return <ShellError />
  }
  if (!platform || !catalogs) {
    return null
  }
  return (
    <PlatformContext.Provider value={platform}>
      <UnsavedChangesGuard>
        <PlatformRoutes />
        <TextProvider catalogs={catalogs} development={development}>
          <SignInPrompt />
          <LeaveWithoutSaving />
        </TextProvider>
      </UnsavedChangesGuard>
    </PlatformContext.Provider>
  )
}
