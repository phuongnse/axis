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

/**
 * Resolves a text key. A key missing from the current locale shows its name in development
 * builds, so it gets noticed, and the fallback-locale text otherwise. A key missing from both
 * locales shows its name.
 */
export function resolveText(key: string, texts: TextMap, fallbackTexts: TextMap, development: boolean): string {
  return texts[key] ?? (development ? key : (fallbackTexts[key] ?? key))
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
