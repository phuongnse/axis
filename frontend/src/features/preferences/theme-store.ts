import { useSyncExternalStore } from 'react';

export const THEME_STORAGE_KEY = 'axis.theme';
export const DEFAULT_THEME_MODE = 'system';

export const supportedThemeModes = [
  { value: 'system', labelKey: 'app.themeSystem' },
  { value: 'light', labelKey: 'app.themeLight' },
  { value: 'dark', labelKey: 'app.themeDark' },
] as const;

export type ThemeMode = (typeof supportedThemeModes)[number]['value'];
export type ResolvedTheme = 'light' | 'dark';

interface ThemePreferenceSnapshot {
  mode: ThemeMode;
  resolvedTheme: ResolvedTheme;
}

const supportedThemeModeValues = new Set<string>(
  supportedThemeModes.map((themeMode) => themeMode.value),
);
const systemThemeQuery = '(prefers-color-scheme: dark)';
const listeners = new Set<() => void>();

let systemPreferenceQuery: MediaQueryList | null = null;
let unsubscribeSystemPreference: (() => void) | null = null;

export function isSupportedThemeMode(value: unknown): value is ThemeMode {
  return typeof value === 'string' && supportedThemeModeValues.has(value);
}

function safeStorageGet(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function safeStorageSet(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Storage can be unavailable in privacy modes; theme still works in memory.
  }
}

function browserThemeQuery(): MediaQueryList | null {
  try {
    return window.matchMedia?.(systemThemeQuery) ?? null;
  } catch {
    return null;
  }
}

function browserPrefersDark(): boolean {
  return browserThemeQuery()?.matches ?? false;
}

export function readStoredThemeMode(): ThemeMode | null {
  const stored = safeStorageGet(THEME_STORAGE_KEY);
  return isSupportedThemeMode(stored) ? stored : null;
}

export function resolveInitialThemeMode(): ThemeMode {
  return readStoredThemeMode() ?? DEFAULT_THEME_MODE;
}

export function resolveTheme(mode: ThemeMode): ResolvedTheme {
  if (mode === 'dark') return 'dark';
  if (mode === 'light') return 'light';
  return browserPrefersDark() ? 'dark' : 'light';
}

export function applyDocumentTheme(mode: ThemeMode): ResolvedTheme {
  const resolvedTheme = resolveTheme(mode);
  document.documentElement.classList.toggle('dark', resolvedTheme === 'dark');
  document.documentElement.dataset.themeMode = mode;
  document.documentElement.style.colorScheme = resolvedTheme;
  return resolvedTheme;
}

export function persistThemeMode(mode: ThemeMode): void {
  safeStorageSet(THEME_STORAGE_KEY, mode);
}

let currentThemeMode = resolveInitialThemeMode();
let currentSnapshot: ThemePreferenceSnapshot = {
  mode: currentThemeMode,
  resolvedTheme: applyDocumentTheme(currentThemeMode),
};

function updateSnapshot(mode: ThemeMode, resolvedTheme: ResolvedTheme): boolean {
  if (currentSnapshot.mode === mode && currentSnapshot.resolvedTheme === resolvedTheme) {
    return false;
  }

  currentThemeMode = mode;
  currentSnapshot = { mode, resolvedTheme };
  return true;
}

function emitThemePreferenceChange(): void {
  for (const listener of listeners) {
    listener();
  }
}

function handleSystemPreferenceChange(): void {
  if (currentThemeMode !== 'system') return;

  const resolvedTheme = applyDocumentTheme(currentThemeMode);
  if (updateSnapshot(currentThemeMode, resolvedTheme)) {
    emitThemePreferenceChange();
  }
}

function ensureSystemPreferenceListener(): void {
  const nextQuery = browserThemeQuery();
  if (systemPreferenceQuery === nextQuery && unsubscribeSystemPreference) return;

  unsubscribeSystemPreference?.();
  unsubscribeSystemPreference = null;
  systemPreferenceQuery = nextQuery;

  if (!nextQuery) return;

  if (typeof nextQuery.addEventListener === 'function') {
    nextQuery.addEventListener('change', handleSystemPreferenceChange);
    unsubscribeSystemPreference = () => {
      nextQuery.removeEventListener('change', handleSystemPreferenceChange);
    };
    return;
  }

  nextQuery.addListener(handleSystemPreferenceChange);
  unsubscribeSystemPreference = () => {
    nextQuery.removeListener(handleSystemPreferenceChange);
  };
}

export function setThemeMode(mode: ThemeMode, options: { persist?: boolean } = {}): void {
  if (!isSupportedThemeMode(mode)) return;

  const resolvedTheme = applyDocumentTheme(mode);
  if (options.persist !== false) {
    persistThemeMode(mode);
  }
  const changed = updateSnapshot(mode, resolvedTheme);
  ensureSystemPreferenceListener();
  if (changed) {
    emitThemePreferenceChange();
  }
}

function subscribeThemePreference(listener: () => void): () => void {
  listeners.add(listener);
  ensureSystemPreferenceListener();

  return () => {
    listeners.delete(listener);
  };
}

function getThemePreferenceSnapshot(): ThemePreferenceSnapshot {
  return currentSnapshot;
}

export function useThemePreference(): ThemePreferenceSnapshot {
  return useSyncExternalStore(
    subscribeThemePreference,
    getThemePreferenceSnapshot,
    getThemePreferenceSnapshot,
  );
}
