import { createContext, useContext } from 'react'

/** A flat map from text key to text, for one locale. */
export type TextMap = Record<string, string>

export async function fetchTexts(locale: string, signal?: AbortSignal): Promise<TextMap> {
  const response = await fetch(`/api/texts/${encodeURIComponent(locale)}`, { signal })
  if (!response.ok) {
    throw new Error(`Loading the texts of '${locale}' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { texts: TextMap }
  return body.texts
}

/** Loads the texts of an application site for one locale. */
export async function fetchSiteTexts(path: string, locale: string, signal?: AbortSignal): Promise<TextMap> {
  const response = await fetch(`/api/sites/${encodeURIComponent(path)}/texts/${encodeURIComponent(locale)}`, {
    signal,
  })
  if (!response.ok) {
    throw new Error(`Loading the texts of site '${path}' in '${locale}' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { texts: TextMap }
  return body.texts
}

/** The texts of one site: the current locale and the fallback locale. */
export interface TextCatalog {
  texts: TextMap
  fallbackTexts: TextMap
}

/**
 * Resolves a text key through the catalogs in order, such as the site texts and then the platform
 * texts. Within a catalog, a key missing from the current locale shows its name in development
 * builds, so it gets noticed, and the fallback-locale text otherwise. A key missing from both
 * locales of a catalog moves on to the next catalog. A key no catalog has shows its name.
 */
export function resolveText(key: string, catalogs: readonly TextCatalog[], development: boolean): string {
  for (const { texts, fallbackTexts } of catalogs) {
    if (Object.hasOwn(texts, key)) {
      return texts[key]
    }
    if (Object.hasOwn(fallbackTexts, key)) {
      return development ? key : fallbackTexts[key]
    }
  }
  return key
}

export const TextContext = createContext<((key: string) => string) | null>(null)

/** Returns the function that turns a text key into the text of the current locale. */
export function useText(): (key: string) => string {
  const text = useContext(TextContext)
  if (!text) {
    throw new Error('useText must be used inside TextProvider.')
  }
  return text
}
