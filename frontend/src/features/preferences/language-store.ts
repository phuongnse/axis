export const LANGUAGE_STORAGE_KEY = 'axis.language';
export const DEFAULT_LANGUAGE = 'en';

export const supportedLanguages = [{ value: 'en' }, { value: 'vi' }] as const;

export type SupportedLanguage = (typeof supportedLanguages)[number]['value'];

const supportedLanguageValues = new Set<string>(
  supportedLanguages.map((language) => language.value),
);

export function isSupportedLanguage(value: unknown): value is SupportedLanguage {
  return typeof value === 'string' && supportedLanguageValues.has(value);
}

export function normalizeSupportedLanguage(value: unknown): SupportedLanguage | null {
  if (typeof value !== 'string') return null;
  const base = value.toLowerCase().split('-')[0];
  return isSupportedLanguage(base) ? base : null;
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
    // Storage can be unavailable in privacy modes; language still works in memory.
  }
}

function browserLanguage(): SupportedLanguage | null {
  const candidates = navigator.languages?.length ? navigator.languages : [navigator.language];
  for (const candidate of candidates) {
    const language = normalizeSupportedLanguage(candidate);
    if (language) return language;
  }
  return null;
}

export function readStoredLanguage(): SupportedLanguage | null {
  const stored = safeStorageGet(LANGUAGE_STORAGE_KEY);
  return normalizeSupportedLanguage(stored);
}

export function resolveInitialLanguage(): SupportedLanguage {
  return readStoredLanguage() ?? browserLanguage() ?? DEFAULT_LANGUAGE;
}

export function applyDocumentLanguage(language: SupportedLanguage): void {
  document.documentElement.lang = language;
}

export function persistLanguage(language: SupportedLanguage): void {
  safeStorageSet(LANGUAGE_STORAGE_KEY, language);
}
