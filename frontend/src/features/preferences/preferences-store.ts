import { create } from 'zustand';

import {
  DEFAULT_LANGUAGE,
  SUPPORTED_LANGUAGES,
  type SupportedLanguage,
} from '@/features/preferences/i18n-resources';

export const LANGUAGE_STORAGE_KEY = 'axis.language';
export const THEME_STORAGE_KEY = 'axis.theme';

export const THEME_MODES = ['light', 'dark', 'system'] as const;

export type ThemeMode = (typeof THEME_MODES)[number];
export type ResolvedTheme = 'light' | 'dark';

interface PreferencesState {
  language: SupportedLanguage;
  themeMode: ThemeMode;
  resolvedTheme: ResolvedTheme;
  setLanguage: (language: SupportedLanguage) => void;
  setThemeMode: (themeMode: ThemeMode) => void;
  setResolvedTheme: (resolvedTheme: ResolvedTheme) => void;
}

function canUseBrowserStorage() {
  return typeof window !== 'undefined' && typeof window.localStorage !== 'undefined';
}

function readStorage(key: string): string | null {
  if (!canUseBrowserStorage()) return null;
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStorage(key: string, value: string) {
  if (!canUseBrowserStorage()) return;
  try {
    window.localStorage.setItem(key, value);
  } catch {
    // Preferences are progressive enhancement; storage failures must not block the UI.
  }
}

function isSupportedLanguage(value: string | null): value is SupportedLanguage {
  return SUPPORTED_LANGUAGES.some((language) => language === value);
}

function isThemeMode(value: string | null): value is ThemeMode {
  return THEME_MODES.some((mode) => mode === value);
}

export function readInitialLanguage(): SupportedLanguage {
  const stored = readStorage(LANGUAGE_STORAGE_KEY);
  return isSupportedLanguage(stored) ? stored : DEFAULT_LANGUAGE;
}

export function readInitialThemeMode(): ThemeMode {
  const stored = readStorage(THEME_STORAGE_KEY);
  return isThemeMode(stored) ? stored : 'system';
}

export function getSystemTheme(): ResolvedTheme {
  if (
    typeof window !== 'undefined' &&
    typeof window.matchMedia === 'function' &&
    window.matchMedia('(prefers-color-scheme: dark)').matches
  ) {
    return 'dark';
  }
  return 'light';
}

export function resolveThemeMode(themeMode: ThemeMode): ResolvedTheme {
  return themeMode === 'system' ? getSystemTheme() : themeMode;
}

export function persistLanguage(language: SupportedLanguage) {
  writeStorage(LANGUAGE_STORAGE_KEY, language);
}

export function persistThemeMode(themeMode: ThemeMode) {
  writeStorage(THEME_STORAGE_KEY, themeMode);
}

export function applyDocumentLanguage(language: SupportedLanguage) {
  if (typeof document === 'undefined') return;
  document.documentElement.lang = language;
}

export function applyDocumentTheme(resolvedTheme: ResolvedTheme, themeMode: ThemeMode) {
  if (typeof document === 'undefined') return;
  document.documentElement.classList.toggle('dark', resolvedTheme === 'dark');
  document.documentElement.dataset.themeMode = themeMode;
  document.documentElement.style.colorScheme = resolvedTheme;
}

const initialThemeMode = readInitialThemeMode();

export const usePreferencesStore = create<PreferencesState>((set) => ({
  language: readInitialLanguage(),
  themeMode: initialThemeMode,
  resolvedTheme: resolveThemeMode(initialThemeMode),
  setLanguage: (language) => set({ language }),
  setThemeMode: (themeMode) =>
    set({
      themeMode,
      resolvedTheme: resolveThemeMode(themeMode),
    }),
  setResolvedTheme: (resolvedTheme) => set({ resolvedTheme }),
}));
