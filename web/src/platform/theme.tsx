import { ConfigProvider } from 'antd'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { antThemeFor } from './antTheme'
import { colourTokens } from './designTokens'
import { ThemeModeContext, type ThemeMode } from './themeMode'
import { useReducedMotion } from './useReducedMotion'

const storageKey = 'axis.themeMode'

function storedMode(): ThemeMode {
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

interface ThemeModeProviderProps {
  /** Starts in this mode, ignoring the stored choice and the system setting. Design-system previews fix it. */
  initialMode?: ThemeMode
  children: ReactNode
}

export function ThemeModeProvider({ initialMode, children }: ThemeModeProviderProps) {
  const [mode, setMode] = useState<ThemeMode>(() => initialMode ?? storedMode())

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
  const reducedMotion = useReducedMotion()
  const config = useMemo(() => antThemeFor(mode, undefined, reducedMotion), [mode, reducedMotion])

  // Proposed: the page behind the shell is `canvas` in both modes, so no white shows past its edges or when the page
  // overscrolls. A design-system preview fixes its mode and leaves the document of its card alone.
  useEffect(() => {
    if (initialMode === undefined) {
      document.body.style.background = colourTokens.canvas[mode]
    }
  }, [initialMode, mode])

  return (
    <ThemeModeContext.Provider value={value}>
      <ConfigProvider theme={config}>{children}</ConfigProvider>
    </ThemeModeContext.Provider>
  )
}
