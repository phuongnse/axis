import { ConfigProvider } from 'antd'
import { useMemo, useSyncExternalStore, type ReactNode } from 'react'
import { antThemeFor } from '../platform/antTheme'
import { ThemeModeContext, type ThemeMode } from '../platform/themeMode'

function subscribe(onChange: () => void): () => void {
  const observer = new MutationObserver(onChange)
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] })
  return () => observer.disconnect()
}

/** The page owns the mode: `data-theme="dark"` on the root element is dark, anything else is light. */
function pageMode(): ThemeMode {
  return document.documentElement.getAttribute('data-theme') === 'dark' ? 'dark' : 'light'
}

/** Draws its children in the mode the page sets with `data-theme`, and redraws when the attribute changes. */
export function PageTheme({ children }: { children: ReactNode }) {
  const mode = useSyncExternalStore(subscribe, pageMode)
  // The page owns the mode, so a widget's request to toggle it changes nothing.
  const value = useMemo(() => ({ mode, toggleMode: () => {} }), [mode])
  const config = useMemo(() => antThemeFor(mode), [mode])

  return (
    <ThemeModeContext.Provider value={value}>
      <ConfigProvider theme={config}>{children}</ConfigProvider>
    </ThemeModeContext.Provider>
  )
}
