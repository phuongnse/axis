import { useEffect, useState } from 'react'
import { fetchSiteTexts, type TextCatalog } from './texts'

const noCatalogs: ReadonlyMap<string, TextCatalog> = new Map()

const requestKey = (sitePath: string, ids: string, locale: string, fallbackLocale: string) =>
  `${sitePath}|${ids}|${locale}|${fallbackLocale}`

/**
 * Loads the texts of the releases a page shows work from, for the current and the fallback locale.
 * It fetches each distinct release once. `catalogs` maps a release id to its texts. The map is
 * replaced only when every release has loaded, so the previous texts stay while a new locale or
 * set of releases loads. `failed` is set when any request failed, until the next request starts.
 */
export function useReleaseTexts(
  sitePath: string,
  releaseIds: readonly string[],
  locale: string,
  fallbackLocale: string,
): { catalogs: ReadonlyMap<string, TextCatalog>; failed: boolean } {
  const ids = [...new Set(releaseIds)].sort().join(',')
  const request = requestKey(sitePath, ids, locale, fallbackLocale)
  const [catalogs, setCatalogs] = useState(noCatalogs)
  // The request that failed, so the next request starts without the failure.
  const [failedRequest, setFailedRequest] = useState<string>()

  useEffect(() => {
    if (ids === '') {
      return
    }
    const controller = new AbortController()
    const { signal } = controller
    Promise.all(
      ids.split(',').map(async (id) => {
        const [fallbackTexts, texts] = await Promise.all([
          fetchSiteTexts(sitePath, fallbackLocale, signal, id),
          locale === fallbackLocale ? undefined : fetchSiteTexts(sitePath, locale, signal, id),
        ])
        return [id, { texts: texts ?? fallbackTexts, fallbackTexts }] as const
      }),
    )
      .then((entries) => setCatalogs(new Map(entries)))
      .catch((error: unknown) => {
        if (!signal.aborted) {
          console.warn(`Loading the texts of the releases '${ids}' of '${sitePath}' failed`, error)
          setFailedRequest(requestKey(sitePath, ids, locale, fallbackLocale))
        }
      })
    return () => controller.abort()
  }, [sitePath, ids, locale, fallbackLocale])

  return { catalogs, failed: failedRequest === request }
}
