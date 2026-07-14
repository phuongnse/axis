import { act, cleanup, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useTranslation } from 'react-i18next';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/features/auth/auth-store';
import {
  changeSiteLanguage,
  PreferencesMenu,
  PreferencesProfileSync,
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

function deferredResponse() {
  let resolve!: (response: Response) => void;
  const promise = new Promise<Response>((resolver) => {
    resolve = resolver;
  });
  return { promise, resolve };
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

function TranslatedProfileHarness() {
  const { t } = useTranslation();

  return (
    <>
      <PreferencesProfileSync />
      <p>{t('dashboard.accountReady')}</p>
    </>
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
    cleanup();
    useAuthStore.getState().clearSession();
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

  it('persists authenticated theme selection through the API', async () => {
    const user = userEvent.setup();
    const themeSave = deferredResponse();
    useAuthStore.getState().setSession('header.payload.signature');
    vi.mocked(fetch).mockReturnValue(themeSave.promise);

    await renderWithRouter(<ThemeControl authenticated variant="menu" />, {
      path: '/dashboard',
    });
    await user.click(screen.getByRole('button', { name: 'Dark' }));

    expect(document.documentElement.dataset.themeMode).toBe('dark');
    expect(screen.getByText('Saving...')).toHaveClass('absolute', 'top-0', 'right-1');
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
    themeSave.resolve(jsonResponse({ theme: 'dark' }));
    await waitFor(() =>
      expect(document.querySelector('#theme-save-status')).not.toBeInTheDocument(),
    );
    expect(screen.getByRole('button', { name: 'Dark' })).toHaveAttribute('aria-pressed', 'true');
    const request = vi.mocked(fetch).mock.calls[0][1];
    expect(request?.method).toBe('PUT');
    expect(String(request?.body)).toContain('"theme":"dark"');
  });

  it('applies authenticated server theme as source of truth and mirrors it to storage', async () => {
    useAuthStore.getState().setSession('header.payload.signature');
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input.toString();
      if (url.includes('/api/users/me')) {
        return Promise.resolve(
          jsonResponse({
            id: '9fc0f6c1-24f6-4e66-a50f-3f742ad10b1a',
            email: 'admin@example.com',
            fullName: 'Admin User',
            isActive: true,
            language: 'en',
            theme: 'dark',
            workspaceId: null,
            workspaces: [],
          }),
        );
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<TranslatedProfileHarness />, { path: '/dashboard' });

    await waitFor(() => expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark'));
    expect(await screen.findByText('Account ready')).toBeInTheDocument();
    expect(document.documentElement).toHaveClass('dark');
    expect(document.documentElement.dataset.themeMode).toBe('dark');
  });

  it('keeps selected authenticated theme usable and shows retry state when persistence fails', async () => {
    const user = userEvent.setup();
    useAuthStore.getState().setSession('header.payload.signature');
    vi.mocked(fetch).mockResolvedValue({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
      json: () => Promise.resolve({ detail: 'boom' }),
    } as unknown as Response);

    await renderWithRouter(<ThemeControl authenticated />, { path: '/dashboard' });
    await user.click(screen.getByRole('button', { name: 'Dark' }));

    expect(document.documentElement.dataset.themeMode).toBe('dark');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark');
    expect(await screen.findByText('Theme not saved across devices.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument();
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
