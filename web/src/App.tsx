import { useEffect, useMemo, useState } from 'react'
import { Route, Routes } from 'react-router'
import { AccountPage } from './pages/AccountPage'
import { ConsoleInstancePage } from './pages/ConsoleInstancePage'
import { ConsolePage } from './pages/ConsolePage'
import { HomePage } from './pages/HomePage'
import { ShellError } from './pages/LocaleLoadFailed'
import { LicencesPage } from './pages/LicencesPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { PageRoute } from './pages/PageRoute'
import { PlatformContext, usePlatform, type Platform, type PlatformTexts } from './pages/context'
import { PlatformShellRoute } from './pages/PlatformShellRoute'
import { SiteIndex, SiteRoute } from './pages/SiteRoute'
import { StudioPage } from './pages/StudioPage'
import { UsersPage } from './pages/UsersPage'
import { readStoredLocale } from './platform/locale'
import { SignInPrompt } from './platform/SignInPrompt'
import { TextProvider } from './platform/TextProvider'
import { fetchSite, type SiteMetadata } from './platform/site'
import { setHostSite } from './platform/siteLinks'
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
      <PlatformRoutes />
      <TextProvider catalogs={catalogs} development={development}>
        <SignInPrompt />
      </TextProvider>
    </PlatformContext.Provider>
  )
}

/**
 * The routes of the platform. On the host of a site the SPA serves that site at `/` and its pages at `/{page}`, and
 * nothing else, as the server serves nothing else there. Elsewhere it serves the platform's own routes and every site
 * under `/{site}`.
 */
function PlatformRoutes() {
  const { hostSite } = usePlatform().site
  if (hostSite) {
    return (
      <Routes>
        <Route element={<SiteRoute path={hostSite} />}>
          <Route index element={<SiteIndex />} />
          <Route path=":page" element={<PageRoute />} />
          <Route path=":page/new" element={<PageRoute form="new" />} />
          <Route path=":page/:id" element={<PageRoute form="edit" />} />
          <Route path=":module/:page/new" element={<PageRoute form="new" />} />
          <Route path=":module/:page/:id" element={<PageRoute form="edit" />} />
        </Route>
        <Route
          path="*"
          element={
            <PlatformShellRoute>
              <NotFoundPage />
            </PlatformShellRoute>
          }
        />
      </Routes>
    )
  }
  return (
    <Routes>
      <Route
        index
        element={
          <PlatformShellRoute>
            <HomePage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="studio"
        element={
          <PlatformShellRoute>
            <StudioPage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="account"
        element={
          <PlatformShellRoute>
            <AccountPage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="users"
        element={
          <PlatformShellRoute>
            <UsersPage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="console"
        element={
          <PlatformShellRoute>
            <ConsolePage />
          </PlatformShellRoute>
        }
      />
      <Route
        path="console/instances/:id"
        element={
          <PlatformShellRoute>
            <ConsoleInstancePage />
          </PlatformShellRoute>
        }
      />
      {/* Proposed: the Open source licences page, open without signing in. `licences` becomes a reserved site path. */}
      <Route
        path="licences"
        element={
          <PlatformShellRoute>
            <LicencesPage />
          </PlatformShellRoute>
        }
      />
      <Route path=":site" element={<SiteRoute />}>
        <Route index element={<SiteIndex />} />
        <Route path=":page" element={<PageRoute />} />
        <Route path=":page/new" element={<PageRoute form="new" />} />
        <Route path=":page/:id" element={<PageRoute form="edit" />} />
        <Route path=":module/:page/new" element={<PageRoute form="new" />} />
        <Route path=":module/:page/:id" element={<PageRoute form="edit" />} />
      </Route>
      <Route
        path="*"
        element={
          <PlatformShellRoute>
            <NotFoundPage />
          </PlatformShellRoute>
        }
      />
    </Routes>
  )
}

/** The routes of the SPA. The caller provides the router, so tests can start at any address. */
export function App({ development }: { development: boolean }) {
  return (
    <Routes>
      <Route path="*" element={<PlatformRoot development={development} />} />
    </Routes>
  )
}
