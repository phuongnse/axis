import '@testing-library/jest-dom';
import { beforeEach } from 'vitest';

import { useAuthStore } from '@/features/auth/auth-store';
import { changeSiteLanguage, DEFAULT_LANGUAGE, setThemeMode } from '@/features/preferences';

Object.defineProperty(window, 'scrollTo', {
  configurable: true,
  value: () => undefined,
});

beforeEach(async () => {
  useAuthStore.getState().clearSession();
  localStorage.removeItem('axis.language');
  localStorage.removeItem('axis.theme');
  await changeSiteLanguage(DEFAULT_LANGUAGE, { persist: false });
  setThemeMode('system', { persist: false });
  document.documentElement.lang = DEFAULT_LANGUAGE;
});
