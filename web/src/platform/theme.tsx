import { ConfigProvider } from 'antd'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { antThemeFor } from './antTheme'
import { designTokensFor } from './designTokens'
import { dialogButtons } from './dialogButtons'
import { ThemeModeContext, type ThemeChoice, type ThemeMode } from './themeMode'
import { useReducedMotion } from './useReducedMotion'
import { useSystemThemeMode } from './useSystemThemeMode'

const storageKey = 'axis.themeMode'

function storedChoice(): ThemeChoice {
  try {
    const stored = localStorage.getItem(storageKey)
    if (stored === 'system' || stored === 'light' || stored === 'dark') {
      return stored
    }
  } catch {
    // Storage can be unavailable (private mode); the choice is then System.
  }
  return 'system'
}

interface ThemeModeProviderProps {
  /** Starts in this mode, ignoring the stored choice and the system setting. Design-system previews fix it. */
  initialMode?: ThemeMode
  children: ReactNode
}

export function ThemeModeProvider({ initialMode, children }: ThemeModeProviderProps) {
  const [choice, setChoice] = useState<ThemeChoice>(() => initialMode ?? storedChoice())
  const systemMode = useSystemThemeMode()
  const mode = choice === 'system' ? systemMode : choice

  const choose = useCallback((next: ThemeChoice) => {
    setChoice(next)
    try {
      localStorage.setItem(storageKey, next)
    } catch {
      // The choice is still applied for this session.
    }
  }, [])

  const value = useMemo(() => ({ mode, choice, choose }), [mode, choice, choose])
  const reducedMotion = useReducedMotion()
  const config = useMemo(() => antThemeFor(mode, undefined, reducedMotion), [mode, reducedMotion])

  useEffect(() => {
    document.body.style.backgroundColor = designTokensFor(mode).colour.canvas
  }, [mode])

  return (
    <ThemeModeContext.Provider value={value}>
      {/* Proposed: beside a dialog's solid button, Cancel is a text button, as every action beside a solid one. */}
      <ConfigProvider theme={config} modal={dialogButtons}>
        {children}
      </ConfigProvider>
    </ThemeModeContext.Provider>
  )
}
