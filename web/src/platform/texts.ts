import { createContext, useContext } from 'react'
import { apiFetch } from './api'

/** A flat map from text key to text, for one locale. */
export type TextMap = Record<string, string>

export async function fetchTexts(locale: string, signal?: AbortSignal): Promise<TextMap> {
  const response = await apiFetch(`/api/texts/${encodeURIComponent(locale)}`, { signal })
  if (!response.ok) {
    throw new Error(`Loading the texts of '${locale}' failed with status ${response.status}.`)
  }
  const body = (await response.json()) as { texts: TextMap }
  return body.texts
}

/**
 * Loads the texts of an application site for one locale. With `release`, it loads the texts of that
 * stored release of the site's application, active or not, instead of the active release's.
 */
export async function fetchSiteTexts(
  path: string,
  locale: string,
  signal?: AbortSignal,
  release?: string,
): Promise<TextMap> {
  const query = release === undefined ? '' : `?release=${encodeURIComponent(release)}`
  const response = await apiFetch(
    `/api/sites/${encodeURIComponent(path)}/texts/${encodeURIComponent(locale)}${query}`,
    { signal },
  )
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

/** The catalogs a key resolves through, and whether a missing current-locale key shows its name. */
export interface TextCatalogs {
  catalogs: readonly TextCatalog[]
  development: boolean
}

export const TextCatalogsContext = createContext<TextCatalogs | null>(null)

/** Returns the catalogs behind {@link useText}, to resolve a key through more catalogs first. */
export function useTextCatalogs(): TextCatalogs {
  const catalogs = useContext(TextCatalogsContext)
  if (!catalogs) {
    throw new Error('useTextCatalogs must be used inside TextProvider.')
  }
  return catalogs
}

/**
 * Proposed: a text with a word that Axis defines, such as a type's or a kind's name, put in place of `placeholder`. The
 * word follows the sentence's capitals: it starts with a capital where the placeholder starts the text, and in lower
 * case elsewhere, so "{type} settings" reads "Text settings" and "Thiết lập {type}" reads "Thiết lập văn bản". A name an
 * author gave, such as an entity's label, is not put in this way: it keeps its own capitals.
 */
export function withWord(template: string, placeholder: string, word: string): string {
  const atStart = template.trimStart().startsWith(placeholder)
  const cased = atStart
    ? word.charAt(0).toLocaleUpperCase() + word.slice(1)
    : word.charAt(0).toLocaleLowerCase() + word.slice(1)
  return template.replace(placeholder, cased)
}
