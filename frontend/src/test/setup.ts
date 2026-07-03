import '@testing-library/jest-dom';
import { beforeEach } from 'vitest';

import { useAuthStore } from '@/features/auth/auth-store';
import { changeSiteLanguage } from '@/features/preferences';

beforeEach(async () => {
  useAuthStore.getState().clearSession();
  await changeSiteLanguage('en', { persist: false });
  document.documentElement.lang = 'en';
  document.documentElement.classList.remove('dark');
  document.documentElement.style.colorScheme = 'light';
  localStorage.removeItem('axis.language');
  localStorage.removeItem('axis.theme');
});
