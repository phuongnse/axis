import { ConfigProvider, theme, type ThemeConfig } from 'antd'
import { useCallback, useMemo, useState, type ReactNode } from 'react'
import { ThemeModeContext, type ThemeMode } from './themeMode'

const storageKey = 'axis.themeMode'

// Shared design tokens. Feature code never sets colours, spacing or typography itself.
const sharedTokens: ThemeConfig['token'] = {
  colorPrimary: '#2f54eb',
  borderRadius: 6,
  fontFamily: "'Inter', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
}

function initialMode(): ThemeMode {
  try {
    const stored = localStorage.getItem(storageKey)
    if (stored === 'light' || stored === 'dark') {
      return stored
    }
  } catch {
    // Storage can be unavailable (private mode); fall back to the system preference.
  }
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export function ThemeModeProvider({ children }: { children: ReactNode }) {
  const [mode, setMode] = useState<ThemeMode>(initialMode)

  const toggleMode = useCallback(() => {
    setMode((current) => {
      const next = current === 'dark' ? 'light' : 'dark'
      try {
        localStorage.setItem(storageKey, next)
      } catch {
        // The choice is still applied for this session.
      }
      return next
    })
  }, [])

  const value = useMemo(() => ({ mode, toggleMode }), [mode, toggleMode])
  const config = useMemo<ThemeConfig>(
    () => ({
      algorithm: mode === 'dark' ? theme.darkAlgorithm : theme.defaultAlgorithm,
      token: sharedTokens,
    }),
    [mode],
  )

  return (
    <ThemeModeContext.Provider value={value}>
      <ConfigProvider theme={config}>{children}</ConfigProvider>
    </ThemeModeContext.Provider>
  )
}
