import type { SyntheticEvent } from 'react'
import type { StudioDiagnostic, StudioNames } from '../platform/studio'
import type { TextCatalog } from '../platform/texts'

/** How long after the last edit a canvas asks for the draft's metadata. */
export const debounceMs = 300

/** A click or a submit on the canvas runs nothing: not a link, not a button and not a form. */
export function cancel(event: SyntheticEvent) {
  event.preventDefault()
  event.stopPropagation()
}

/**
 * The application's own texts, from Studio's names: the platform locale when the application has it, otherwise its
 * first locale, which is also the fallback. The platform texts come after, through the outer catalogs.
 */
export function canvasTexts(names: StudioNames | undefined, platformLocale: string | undefined) {
  const fallbackLocale = names?.locales[0]
  if (!names || fallbackLocale === undefined) {
    return null
  }
  const locale =
    platformLocale !== undefined && names.locales.includes(platformLocale) ? platformLocale : fallbackLocale
  const catalog: TextCatalog = { texts: {}, fallbackTexts: {} }
  for (const item of names.texts) {
    if (Object.hasOwn(item.texts, locale)) {
      catalog.texts[item.key] = item.texts[locale]
    }
    if (Object.hasOwn(item.texts, fallbackLocale)) {
      catalog.fallbackTexts[item.key] = item.texts[fallbackLocale]
    }
  }
  return { catalog, locale, fallbackLocale }
}

/** The diagnostics that name the widget at `path`, or a path inside it. None when the widget has no path. */
export function diagnosticsAt(diagnostics: StudioDiagnostic[], path: string | undefined): StudioDiagnostic[] {
  if (path === undefined) {
    return []
  }
  return diagnostics.filter((diagnostic) => diagnostic.path === path || diagnostic.path.startsWith(`${path}/`))
}
