import { useEffect, useMemo, useState } from 'react'
import { Outlet, Route, Routes } from 'react-router'
import { HomePage } from './pages/HomePage'
import { ShellError } from './pages/LocaleLoadFailed'
import { NotFoundPage } from './pages/NotFoundPage'
import { PageRoute } from './pages/PageRoute'
import { PlatformContext, type Platform, type PlatformTexts } from './pages/context'
import { PlatformShellRoute } from './pages/PlatformShellRoute'
import { SiteIndex, SiteRoute } from './pages/SiteRoute'
import { readStoredLocale } from './platform/locale'
import { fetchSite, type SiteMetadata } from './platform/site'
import { fetchTexts, type TextMap } from './platform/texts'

interface LoadedPlatform {
  site: SiteMetadata
  fallbackTexts: TextMap
  current: PlatformTexts
}

/** Loads the platform site and its texts once, then renders the matching route. */
function PlatformRoot({ development }: { development: boolean }) {
  const [loaded, setLoaded] = useState<LoadedPlatform>()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      const site = await fetchSite(controller.signal)
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

  if (failed) {
    return <ShellError />
  }
  if (!platform) {
    return null
  }
  return (
    <PlatformContext.Provider value={platform}>
      <Outlet />
    </PlatformContext.Provider>
  )
}

/** The routes of the SPA. The caller provides the router, so tests can start at any address. */
export function App({ development }: { development: boolean }) {
  return (
    <Routes>
      <Route element={<PlatformRoot development={development} />}>
        <Route
          index
          element={
            <PlatformShellRoute>
              <HomePage />
            </PlatformShellRoute>
          }
        />
        <Route path=":site" element={<SiteRoute />}>
          <Route index element={<SiteIndex />} />
          <Route path=":page" element={<PageRoute />} />
          <Route path=":page/new" element={<PageRoute form="new" />} />
          <Route path=":page/:id" element={<PageRoute form="edit" />} />
        </Route>
        <Route
          path="*"
          element={
            <PlatformShellRoute>
              <NotFoundPage />
            </PlatformShellRoute>
          }
        />
      </Route>
    </Routes>
  )
}
