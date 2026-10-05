import { createContext, useContext } from 'react'

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
