import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Navigate, Outlet, useParams } from 'react-router'
import { readStoredLocale, storeLocale } from '../platform/locale'
import { Shell } from '../platform/Shell'
import { fetchApplicationSite, type ApplicationSite } from '../platform/site'
import { TextProvider } from '../platform/TextProvider'
import { fetchSiteTexts, type TextMap } from '../platform/texts'
import { LocaleLoadFailed, ShellError } from './LocaleLoadFailed'
import { NotFoundPage } from './NotFoundPage'
import { platformTextsFor, useApplicationSite, usePlatform, type SiteOutlet } from './context'
import { PlatformShellRoute } from './PlatformShellRoute'

interface LoadedSite {
  site: ApplicationSite
  locale: string
  texts: TextMap
  fallbackTexts: TextMap
  platformTexts: TextMap
}

/** Opens the application site named by the `:site` segment. */
export function SiteRoute() {
  const { site = '' } = useParams()
  // A new site starts from a clean state rather than showing the previous site meanwhile.
  return <SiteShell key={site} path={site} />
}

function SiteShell({ path }: { path: string }) {
  const platform = usePlatform()
  const [loaded, setLoaded] = useState<LoadedSite | 'missing' | 'failed'>()
  const [localeFailed, setLocaleFailed] = useState(false)
  const localeRequest = useRef<AbortController>(undefined)
  // The site loads once per path, with the platform as it is then. A later platform locale
  // change does not reload the site.
  const platformRef = useRef(platform)
  useEffect(() => {
    platformRef.current = platform
  })

  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      const site = await fetchApplicationSite(path, controller.signal)
      if (!site) {
        setLoaded('missing')
        return
      }
      const { signal } = controller
      const locale = readStoredLocale(site.locales.available, site.locales.default)
      const [fallbackTexts, texts, platformTexts] = await Promise.all([
        fetchSiteTexts(site.path, site.locales.fallback, signal),
        locale === site.locales.fallback ? undefined : fetchSiteTexts(site.path, locale, signal),
        platformTextsFor(platformRef.current, locale, signal),
      ])
      setLoaded({ site, locale, texts: texts ?? fallbackTexts, fallbackTexts, platformTexts })
    }
    load().catch((error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn(`Loading the site '${path}' failed`, error)
        setLoaded('failed')
      }
    })
    return () => {
      controller.abort()
      localeRequest.current?.abort()
    }
  }, [path])

  // Keeps the current texts until the new locale has loaded, and drops a superseded switch.
  const changeLocale = useCallback(
    (locale: string) => {
      if (typeof loaded !== 'object') {
        return
      }
      localeRequest.current?.abort()
      const controller = new AbortController()
      localeRequest.current = controller
      setLocaleFailed(false)
      const { site, fallbackTexts } = loaded
      Promise.all([
        locale === site.locales.fallback ? fallbackTexts : fetchSiteTexts(site.path, locale, controller.signal),
        platformTextsFor(platform, locale, controller.signal),
      ])
        .then(([texts, platformTexts]) => {
          storeLocale(locale)
          setLoaded((previous) =>
            typeof previous === 'object' ? { ...previous, locale, texts, platformTexts } : previous,
          )
          // The platform shell follows the choice when it offers the locale too.
          if (platform.site.locales.available.includes(locale)) {
            platform.setCurrent({ locale, texts: platformTexts })
          }
        })
        .catch((error: unknown) => {
          if (!controller.signal.aborted) {
            console.warn(`Loading the texts of '${locale}' failed`, error)
            setLocaleFailed(true)
          }
        })
    },
    [loaded, platform],
  )

  const catalogs = useMemo(
    () =>
      typeof loaded === 'object'
        ? [
            { texts: loaded.texts, fallbackTexts: loaded.fallbackTexts },
            { texts: loaded.platformTexts, fallbackTexts: platform.fallbackTexts },
          ]
        : [],
    [loaded, platform.fallbackTexts],
  )

  if (loaded === 'missing') {
    return (
      <PlatformShellRoute>
        <NotFoundPage />
      </PlatformShellRoute>
    )
  }
  if (loaded === 'failed') {
    return <ShellError />
  }
  if (!loaded) {
    return null
  }

  const { site, locale } = loaded
  return (
    <TextProvider catalogs={catalogs} development={platform.development}>
      <Shell
        titleKey={site.titleKey}
        navigation={site.navigation.map((item) => ({
          key: item.page,
          path: `/${site.path}/${item.page.toLowerCase()}`,
          labelKey: item.labelKey,
        }))}
        locales={site.locales.available}
        locale={locale}
        onLocaleChange={changeLocale}
      >
        {localeFailed && <LocaleLoadFailed onClose={() => setLocaleFailed(false)} />}
        <Outlet context={{ site, locale } satisfies SiteOutlet} />
      </Shell>
    </TextProvider>
  )
}

/** `/{site}` opens the first navigation entry of the site. */
export function SiteIndex() {
  const site = useApplicationSite()
  const first = site.navigation[0]
  return first ? <Navigate to={`/${site.path}/${first.page.toLowerCase()}`} replace /> : <NotFoundPage />
}
