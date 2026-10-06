import { useCallback, type ReactNode } from 'react'
import { resolveText, TextContext, type TextCatalog } from './texts'

interface TextProviderProps {
  /** The catalogs a key resolves through, in order: the site texts, then the platform texts. */
  catalogs: readonly TextCatalog[]
  /** Shows the key name, rather than the fallback text, for a key the current locale lacks. */
  development: boolean
  children: ReactNode
}

export function TextProvider({ catalogs, development, children }: TextProviderProps) {
  const text = useCallback((key: string) => resolveText(key, catalogs, development), [catalogs, development])
  return <TextContext.Provider value={text}>{children}</TextContext.Provider>
}
