import { ConfigProvider } from 'antd'
import { useMemo, useSyncExternalStore, type ReactNode } from 'react'
import { antThemeFor } from '../platform/antTheme'
import { designTokensFor, pageTokenPropertyNames, type PageTokenValues } from '../platform/designTokens'
import { ThemeModeContext, type ThemeMode } from '../platform/themeMode'
import { useReducedMotion } from '../platform/useReducedMotion'

function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange)
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme', 'style', 'class'] })
  // A stylesheet edit in the head changes the custom properties. Ant Design adds its own styles there too, which
  // re-reads the root element, but the snapshot only changes when a value really changed.
  observer.observe(document.head, { childList: true, subtree: true, characterData: true })
  return () => observer.disconnect()
}

/** The page owns the mode: `data-theme="dark"` on the root element is dark, anything else is light. */
function pageMode(): ThemeMode {
  return document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light'
}

/** The token properties the root element declares, with their raw values. */
function pageValues(): PageTokenValues {
  const style = getComputedStyle(document.documentElement)
  const entries = pageTokenPropertyNames
    .map((property) => [property, style.getPropertyValue(property).trim()] as const)
    .filter(([, value]) => value !== '')
  return Object.fromEntries(entries)
}

/** A string, so that useSyncExternalStore sees the same snapshot until the mode or a value changes. */
function pageSnapshot(): string {
  return JSON.stringify({ mode: pageMode(), values: pageValues() })
}

/**
 * Draws its children in the mode the page sets with `data-theme`, with the token values that the page declares as
 * custom properties on its root element. It redraws when the mode or one of those values changes.
 */
export function PageTheme({ children }: { children: ReactNode }) {
  const snapshot = useSyncExternalStore(subscribe, pageSnapshot)
  const { mode, values } = useMemo(
    () => JSON.parse(snapshot) as { mode: ThemeMode; values: PageTokenValues },
    [snapshot],
  )
  // The page owns the mode, so a widget's request to toggle it changes nothing.
  const value = useMemo(() => ({ mode, toggleMode: () => {}, tokens: designTokensFor(mode, values) }), [mode, values])
  const reducedMotion = useReducedMotion()
  const config = useMemo(() => antThemeFor(mode, values, reducedMotion), [mode, values, reducedMotion])

  return (
    <ThemeModeContext.Provider value={value}>
      <ConfigProvider theme={config}>{children}</ConfigProvider>
    </ThemeModeContext.Provider>
  )
}
