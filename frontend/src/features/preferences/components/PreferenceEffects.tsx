import { useEffect } from 'react';

import { i18n } from '@/features/preferences/i18n';
import {
  applyDocumentLanguage,
  applyDocumentTheme,
  getSystemTheme,
  persistLanguage,
  persistThemeMode,
  usePreferencesStore,
} from '@/features/preferences/preferences-store';

export function PreferenceEffects() {
  const language = usePreferencesStore((state) => state.language);
  const themeMode = usePreferencesStore((state) => state.themeMode);
  const resolvedTheme = usePreferencesStore((state) => state.resolvedTheme);
  const setResolvedTheme = usePreferencesStore((state) => state.setResolvedTheme);

  useEffect(() => {
    void i18n.changeLanguage(language);
    applyDocumentLanguage(language);
    persistLanguage(language);
  }, [language]);

  useEffect(() => {
    applyDocumentTheme(resolvedTheme, themeMode);
    persistThemeMode(themeMode);
  }, [resolvedTheme, themeMode]);

  useEffect(() => {
    if (themeMode !== 'system') return undefined;
    if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return undefined;

    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const handleChange = () => setResolvedTheme(getSystemTheme());
    handleChange();
    media.addEventListener('change', handleChange);

    return () => media.removeEventListener('change', handleChange);
  }, [setResolvedTheme, themeMode]);

  return null;
}
