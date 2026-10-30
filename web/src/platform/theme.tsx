import { ConfigProvider } from 'antd'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { antThemeFor } from './antTheme'
import { colourTokens } from './designTokens'
import { ThemeModeContext, type ThemeMode, type ThemePreference } from './themeMode'
import { useReducedMotion } from './useReducedMotion'

const storageKey = 'axis.themeMode'
const darkQuery = '(prefers-color-scheme: dark)'

/** Proposed: the stored choice, with `system` when there is none, so a first visit follows the operating system. */
function storedPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(storageKey)
    if (stored === 'system' || stored === 'light' || stored === 'dark') {
      return stored
    }
  } catch {
    // Storage can be unavailable (private mode); follow the system.
  }
  return 'system'
}

function systemMode(): ThemeMode {
  return window.matchMedia(darkQuery).matches ? 'dark' : 'light'
}

interface ThemeModeProviderProps {
  /** Starts in this mode, ignoring the stored choice and the system setting. Design-system previews fix it. */
  initialMode?: ThemeMode
  children: ReactNode
}

export function ThemeModeProvider({ initialMode, children }: ThemeModeProviderProps) {
  const [preference, setPreferenceState] = useState<ThemePreference>(() => initialMode ?? storedPreference())
  const [system, setSystem] = useState<ThemeMode>(systemMode)
  const mode: ThemeMode = preference === 'system' ? system : preference

  // Proposed: with `system` chosen, the shell changes mode as soon as the operating system does.
  useEffect(() => {
    const query = window.matchMedia(darkQuery)
    const follow = (event: MediaQueryListEvent) => setSystem(event.matches ? 'dark' : 'light')
    query.addEventListener?.('change', follow)
    return () => query.removeEventListener?.('change', follow)
  }, [])

  const setPreference = useCallback((next: ThemePreference) => {
    setPreferenceState(next)
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // The choice is still applied for this session.
    }
  }, [])

  const toggleMode = useCallback(() => setPreference(mode === 'dark' ? 'light' : 'dark'), [mode, setPreference])

  const value = useMemo(
    () => ({ mode, preference, setPreference, toggleMode }),
    [mode, preference, setPreference, toggleMode],
  )
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
