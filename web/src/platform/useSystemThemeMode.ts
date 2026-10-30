import { useSyncExternalStore } from 'react'
import type { ThemeMode } from './themeMode'

/** The browser setting that asks for dark colours, which follows the operating system. */
export const darkSchemeQuery = '(prefers-color-scheme: dark)'

function subscribe(onChange: () => void) {
  const list = window.matchMedia(darkSchemeQuery)
  list.addEventListener('change', onChange)
  return () => list.removeEventListener('change', onChange)
}

function systemMode(): ThemeMode {
  return window.matchMedia(darkSchemeQuery).matches ? 'dark' : 'light'
}

/** The mode the operating system asks for. It updates when the setting changes while the page is open. */
export function useSystemThemeMode(): ThemeMode {
  return useSyncExternalStore(subscribe, systemMode)
}
