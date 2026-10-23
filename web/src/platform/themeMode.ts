import { createContext, useContext, useMemo } from 'react'
import { designTokensFor } from './designTokens'

export type ThemeMode = 'light' | 'dark'

export interface ThemeModeContextValue {
  mode: ThemeMode
  toggleMode: () => void
}

export const ThemeModeContext = createContext<ThemeModeContextValue | null>(null)

export function useThemeMode(): ThemeModeContextValue {
  const context = useContext(ThemeModeContext)
  if (!context) {
    throw new Error('useThemeMode must be used inside ThemeModeProvider.')
  }
  return context
}

/** The Axis tokens of the current mode. Outside the provider it gives the light tokens. */
export function useDesignTokens() {
  const mode = useContext(ThemeModeContext)?.mode ?? 'light'
  return useMemo(() => designTokensFor(mode), [mode])
}
