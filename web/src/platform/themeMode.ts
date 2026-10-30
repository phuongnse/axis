import { createContext, useContext, useMemo } from 'react'
import { designTokensFor, type DesignTokens } from './designTokens'

export type ThemeMode = 'light' | 'dark'

/** The theme a person picks. `system` follows the operating system's light or dark setting. */
export type ThemeChoice = 'system' | ThemeMode

export interface ThemeModeContextValue {
  /** The mode in use. It is the choice, or the system's setting when the choice is `system`. */
  mode: ThemeMode
  choice: ThemeChoice
  choose: (choice: ThemeChoice) => void
  /** Tokens that already include a page's values. Only the design-system bundle sets them. */
  tokens?: DesignTokens
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
  const context = useContext(ThemeModeContext)
  const mode = context?.mode ?? 'light'
  const tokens = context?.tokens
  return useMemo(() => tokens ?? designTokensFor(mode), [tokens, mode])
}
