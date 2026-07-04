import { act, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useTranslation } from 'react-i18next';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/features/auth/auth-store';
import {
  changeSiteLanguage,
  PreferencesMenu,
  resolveInitialThemeMode,
  setThemeMode,
  THEME_STORAGE_KEY,
  ThemeControl,
} from '@/features/preferences';
import { renderWithRouter } from './render-with-router';

function jsonResponse(data: unknown): Response {
  return {
    ok: true,
    status: 200,
    text: () => Promise.resolve(JSON.stringify(data)),
  } as unknown as Response;
}

function TranslatedFormHarness() {
  const { t } = useTranslation();

  return (
    <form>
      <PreferencesMenu />
      <label htmlFor="email">{t('auth.email')}</label>
      <input id="email" />
    </form>
  );
}

function installColorSchemePreference(matches: boolean) {
  let prefersDark = matches;
  const listeners = new Set<(event: MediaQueryListEvent) => void>();
  const mediaQueryList = {
    media: '(prefers-color-scheme: dark)',
    get matches() {
      return prefersDark;
    },
    onchange: null,
    addEventListener: vi.fn((event: string, listener: (event: MediaQueryListEvent) => void) => {
      if (event === 'change') listeners.add(listener);
    }),
    removeEventListener: vi.fn((event: string, listener: (event: MediaQueryListEvent) => void) => {
      if (event === 'change') listeners.delete(listener);
    }),
    addListener: vi.fn((listener: (event: MediaQueryListEvent) => void) => {
      listeners.add(listener);
    }),
    removeListener: vi.fn((listener: (event: MediaQueryListEvent) => void) => {
      listeners.delete(listener);
    }),
    dispatchEvent: vi.fn(),
  } as unknown as MediaQueryList;

  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => mediaQueryList),
  );

  return {
    setMatches(nextMatches: boolean) {
      prefersDark = nextMatches;
      const event = { matches: nextMatches, media: mediaQueryList.media } as MediaQueryListEvent;
      for (const listener of listeners) {
        listener(event);
      }
    },
  };
}

describe('theme preferences', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('resolves stored theme mode and ignores unsupported storage values', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'dark');
    expect(resolveInitialThemeMode()).toBe('dark');

    localStorage.setItem(THEME_STORAGE_KEY, 'blue');
    expect(resolveInitialThemeMode()).toBe('system');
  });

  it('falls back when storage cannot be read', () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage unavailable');
    });

    expect(resolveInitialThemeMode()).toBe('system');
  });

  it('updates public theme metadata and storage without clearing form state or calling the API', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<TranslatedFormHarness />, { path: '/register' });

    await user.type(screen.getByLabelText('Email address'), 'alex@example.com');
    await user.click(screen.getByRole('button', { name: 'Preferences' }));
    await user.click(screen.getByRole('button', { name: 'Dark' }));

    expect(screen.getByLabelText('Email address')).toHaveValue('alex@example.com');
    expect(document.documentElement).toHaveClass('dark');
    expect(document.documentElement.dataset.themeMode).toBe('dark');
    expect(document.documentElement.style.colorScheme).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    expect(fetch).not.toHaveBeenCalled();
  });

  it('labels the preferences trigger as a shared preferences menu', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<PreferencesMenu />, { path: '/register' });

    const trigger = screen.getByRole('button', { name: 'Preferences' });
    expect(trigger).toHaveTextContent('Preferences');
    expect(trigger).not.toHaveTextContent('EN');
    expect(trigger).not.toHaveTextContent('English');

    await user.click(trigger);

    expect(screen.getByRole('button', { name: 'System' })).toHaveTextContent('System');
    expect(screen.getByRole('button', { name: 'System' })).not.toHaveTextContent('Light');
    expect(screen.getByRole('button', { name: 'Light' })).toHaveTextContent('Light');
    expect(screen.getByRole('button', { name: 'Dark' })).toHaveTextContent('Dark');
  });

  it('keeps selected theme usable when storage cannot be written', async () => {
    const user = userEvent.setup();
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage unavailable');
    });

    await renderWithRouter(<ThemeControl />, { path: '/register' });
    await user.click(screen.getByRole('button', { name: 'Dark' }));

    expect(document.documentElement).toHaveClass('dark');
    expect(document.documentElement.dataset.themeMode).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBeNull();
  });

  it('resolves system mode from browser color scheme and updates while open', async () => {
    const preference = installColorSchemePreference(true);
    setThemeMode('system', { persist: false });

    await renderWithRouter(<PreferencesMenu />, { path: '/register' });

    expect(document.documentElement).toHaveClass('dark');
    expect(document.documentElement.dataset.themeMode).toBe('system');

    act(() => {
      preference.setMatches(false);
    });

    expect(document.documentElement).not.toHaveClass('dark');
    expect(document.documentElement.style.colorScheme).toBe('light');
  });

  it('keeps authenticated theme selection browser-owned', async () => {
    const user = userEvent.setup();
    useAuthStore.getState().setSession('header.payload.signature');
    vi.mocked(fetch).mockResolvedValue(jsonResponse({ language: 'en' }));

    await renderWithRouter(<ThemeControl />, { path: '/dashboard' });
    await user.click(screen.getByRole('button', { name: 'Dark' }));

    expect(document.documentElement.dataset.themeMode).toBe('dark');
    expect(fetch).not.toHaveBeenCalled();
  });

  it('localizes theme controls inside the preferences menu', async () => {
    const user = userEvent.setup();
    await changeSiteLanguage('vi', { persist: false });
    await renderWithRouter(<PreferencesMenu />, { path: '/register' });

    await user.click(screen.getByRole('button', { name: 'Tùy chọn' }));

    expect(screen.getByText('Giao diện')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Tối' }));

    expect(document.documentElement.dataset.themeMode).toBe('dark');
  });
});
