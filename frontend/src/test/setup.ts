import '@testing-library/jest-dom';
import { beforeEach } from 'vitest';

import { i18n } from '@/features/preferences/i18n';
import {
  LANGUAGE_STORAGE_KEY,
  THEME_STORAGE_KEY,
  usePreferencesStore,
} from '@/features/preferences/preferences-store';

beforeEach(async () => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY);
  window.localStorage.removeItem(THEME_STORAGE_KEY);
  usePreferencesStore.getState().setLanguage('en');
  usePreferencesStore.getState().setThemeMode('system');
  await i18n.changeLanguage('en');
  document.documentElement.lang = 'en';
  document.documentElement.classList.remove('dark');
  document.documentElement.dataset.themeMode = 'system';
  document.documentElement.style.colorScheme = 'light';
});
