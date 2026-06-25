import '@testing-library/jest-dom';
import { beforeEach } from 'vitest';

beforeEach(() => {
  document.documentElement.lang = 'en';
  document.documentElement.classList.remove('dark');
  document.documentElement.style.colorScheme = 'light';
});
