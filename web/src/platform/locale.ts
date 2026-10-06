const storageKey = 'axis.locale'

/** Returns the stored locale when the site still offers it, otherwise `fallback`. */
export function readStoredLocale(available: readonly string[], fallback: string): string {
  try {
    const stored = localStorage.getItem(storageKey)
    if (stored && available.includes(stored)) {
      return stored
    }
  } catch {
    // Storage can be unavailable (private mode); use the fallback.
  }
  return fallback
}

export function storeLocale(locale: string): void {
  try {
    localStorage.setItem(storageKey, locale)
  } catch {
    // The choice is still applied for this session.
  }
}
