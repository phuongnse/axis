import { cleanup, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useTranslation } from 'react-i18next';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/features/auth/auth-store';
import {
  LANGUAGE_STORAGE_KEY,
  LanguageControl,
  PreferencesMenu,
  PreferencesProfileSync,
  resolveInitialLanguage,
} from '@/features/preferences';
import { renderWithRouter } from './render-with-router';

function setNavigatorLanguages(languages: string[]) {
  Object.defineProperty(window.navigator, 'languages', {
    configurable: true,
    get: () => languages,
  });
  Object.defineProperty(window.navigator, 'language', {
    configurable: true,
    get: () => languages[0] ?? 'en-US',
  });
}

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

describe('language preferences', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    setNavigatorLanguages(['en-US']);
  });

  afterEach(() => {
    cleanup();
    useAuthStore.getState().clearSession();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('resolves stored language before browser language and ignores unsupported storage values', () => {
    setNavigatorLanguages(['vi-VN']);

    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en');
    expect(resolveInitialLanguage()).toBe('en');

    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'fr');
    expect(resolveInitialLanguage()).toBe('vi');
  });

  it('falls back to browser language when storage cannot be read', () => {
    setNavigatorLanguages(['vi-VN']);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('storage unavailable');
    });

    expect(resolveInitialLanguage()).toBe('vi');
  });

  it('updates public copy, document metadata, and storage without clearing form state or calling the API', async () => {
    const user = userEvent.setup();
    await renderWithRouter(<TranslatedFormHarness />, { path: '/register' });

    await user.type(screen.getByLabelText('Email address'), 'alex@example.com');
    expect(screen.queryByRole('button', { name: 'VI' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'EN' })).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Preferences' }));
    expect(screen.getByRole('button', { name: 'English' })).toBeInTheDocument();
    expect(screen.getByText('EN')).toHaveAttribute('aria-hidden', 'true');
    expect(screen.getByText('VI')).toHaveAttribute('aria-hidden', 'true');
    await user.click(screen.getByRole('button', { name: 'Vietnamese' }));

    expect(screen.getByLabelText('Địa chỉ email')).toHaveValue('alex@example.com');
    expect(screen.getByRole('button', { name: 'Tiếng Anh' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Tiếng Việt' })).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('vi');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('vi');
    expect(fetch).not.toHaveBeenCalled();
  });

  it('applies authenticated server preference as source of truth and mirrors it to storage', async () => {
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
            language: 'vi',
            workspaceId: null,
            workspaces: [],
          }),
        );
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url}`));
    });

    await renderWithRouter(<TranslatedProfileHarness />, { path: '/dashboard' });

    await waitFor(() => expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('vi'));
    expect(await screen.findByText('Tài khoản đã sẵn sàng')).toBeInTheDocument();
    expect(document.documentElement.lang).toBe('vi');
  });

  it('keeps selected authenticated language usable and shows retry state when persistence fails', async () => {
    const user = userEvent.setup();
    useAuthStore.getState().setSession('header.payload.signature');
    vi.mocked(fetch).mockResolvedValue({
      ok: false,
      status: 500,
      statusText: 'Internal Server Error',
      json: () => Promise.resolve({ detail: 'boom' }),
    } as unknown as Response);

    await renderWithRouter(<LanguageControl authenticated />, { path: '/dashboard' });
    await user.click(screen.getByRole('button', { name: 'Vietnamese' }));

    expect(document.documentElement.lang).toBe('vi');
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('vi');
    expect(await screen.findByText('Chưa lưu ngôn ngữ trên các thiết bị.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /thử lại/i })).toBeInTheDocument();

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
    const request = vi.mocked(fetch).mock.calls[0][1];
    expect(request?.method).toBe('PUT');
    expect(String(request?.body)).toContain('"language":"vi"');
  });

  it('ignores stale authenticated language save responses after a newer selection wins', async () => {
    const user = userEvent.setup();
    const vietnameseSave = deferredResponse();
    const englishSave = deferredResponse();
    useAuthStore.getState().setSession('header.payload.signature');
    vi.mocked(fetch).mockImplementation((_input: RequestInfo | URL, init?: RequestInit) => {
      const body = String(init?.body);
      if (body.includes('"language":"vi"')) {
        return vietnameseSave.promise;
      }
      if (body.includes('"language":"en"')) {
        return englishSave.promise;
      }
      return Promise.reject(new Error(`Unexpected language request: ${body}`));
    });

    await renderWithRouter(<LanguageControl authenticated variant="menu" />, {
      path: '/dashboard',
    });

    await user.click(screen.getByRole('button', { name: 'Vietnamese' }));
    expect(screen.getByText('Đang lưu...')).toHaveClass('absolute', 'top-0', 'right-1');
    await user.click(screen.getByRole('button', { name: 'Tiếng Anh' }));

    let staleResponseParsed = false;

    englishSave.resolve(jsonResponse({ language: 'en' }));
    await waitFor(() => expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en'));
    expect(document.querySelector('#language-save-status')).not.toBeInTheDocument();

    vietnameseSave.resolve({
      ok: true,
      status: 200,
      text: () => {
        staleResponseParsed = true;
        return Promise.resolve(JSON.stringify({ language: 'vi' }));
      },
    } as unknown as Response);
    await waitFor(() => expect(staleResponseParsed).toBe(true));
    await waitFor(() => expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('en'));
    expect(document.documentElement.lang).toBe('en');
  });
});
