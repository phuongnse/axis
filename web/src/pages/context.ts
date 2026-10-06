import { createContext, useContext } from 'react'
import { useOutletContext } from 'react-router'
import type { ApplicationSite, SiteMetadata } from '../platform/site'
import { fetchTexts, type TextMap } from '../platform/texts'

/** The texts of the locale the platform shell currently shows. */
export interface PlatformTexts {
  locale: string
  texts: TextMap
}

/** The platform site and its texts, loaded once for every route. */
export interface Platform {
  site: SiteMetadata
  fallbackTexts: TextMap
  current: PlatformTexts
  setCurrent: (current: PlatformTexts) => void
  development: boolean
}

export const PlatformContext = createContext<Platform | null>(null)

export function usePlatform(): Platform {
  const platform = useContext(PlatformContext)
  if (!platform) {
    throw new Error('usePlatform must be used inside PlatformContext.')
  }
  return platform
}

/**
 * Returns the platform texts for a locale. A locale the platform does not offer gets the
 * fallback-locale texts, so the shell still renders inside a site with more locales.
 */
export function platformTextsFor(platform: Platform, locale: string, signal: AbortSignal): Promise<TextMap> {
  if (locale === platform.current.locale) {
    return Promise.resolve(platform.current.texts)
  }
  if (locale === platform.site.locales.fallback || !platform.site.locales.available.includes(locale)) {
    return Promise.resolve(platform.fallbackTexts)
  }
  return fetchTexts(locale, signal)
}

/** The site the current route belongs to, for the routes inside a site. */
export function useApplicationSite(): ApplicationSite {
  return useOutletContext<ApplicationSite>()
}
