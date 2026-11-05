import { useContext } from 'react'
import { StudioNamesContext } from '../platform/studioNames'
import { PlatformContext } from './context'
import { LabelTextsContext } from './labelTextsContext'

/**
 * Proposed: the one way Studio turns a text key into the words people read. It gives the text in the interface's
 * language, else in the folder's first language, or `null` when the key is missing or no text resource has it. A text
 * edited but not saved counts, so a label shows as it will be. Pickers, tables and designers all read their labels
 * through this.
 */
export function useTextOfKey(): (key: string | null | undefined) => string | null {
  const names = useContext(StudioNamesContext)
  const resource = useContext(LabelTextsContext)
  const uiLocale = useContext(PlatformContext)?.current.locale ?? 'en'
  return (key) => {
    if (key === null || key === undefined || key === '') {
      return null
    }
    const texts = resource?.pending?.[key] ?? names?.texts.find((entry) => entry.key === key)?.texts
    if (!texts) {
      return null
    }
    const locale = names?.locales.includes(uiLocale) ? uiLocale : names?.locales[0]
    return (locale && texts[locale]?.trim()) || null
  }
}
