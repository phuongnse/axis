import { useCallback, type ReactNode } from 'react'
import { resolveText, TextContext, type TextMap } from './texts'

interface TextProviderProps {
  texts: TextMap
  fallbackTexts: TextMap
  /** Shows the key name, rather than the fallback text, for a key the current locale lacks. */
  development: boolean
  children: ReactNode
}

export function TextProvider({ texts, fallbackTexts, development, children }: TextProviderProps) {
  const text = useCallback(
    (key: string) => resolveText(key, texts, fallbackTexts, development),
    [texts, fallbackTexts, development],
  )
  return <TextContext.Provider value={text}>{children}</TextContext.Provider>
}
