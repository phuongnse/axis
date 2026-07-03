import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { restoreSessionFromBrowserAuth } from '@/features/auth/api';
import { getAccessToken, useAuthStore } from '@/features/auth/auth-store';
import { ensureAuthenticatedRouteSession } from '@/routes/_authenticated';

function authResponse(state: string): Response {
  return {
    ok: true,
    status: 200,
    url: `${window.location.origin}/callback?code=auth-code&state=${state}`,
  } as unknown as Response;
}

function tokenResponse(token = 'restored-access-token'): Response {
  return {
    ok: true,
    status: 200,
    json: () => Promise.resolve({ access_token: token }),
  } as unknown as Response;
}

function unauthenticatedResponse(): Response {
  return {
    ok: false,
    status: 401,
    url: '',
  } as unknown as Response;
}

function localStorageValues(): string[] {
  return Array.from({ length: localStorage.length }, (_, index) => localStorage.key(index))
    .filter((key): key is string => key !== null)
    .map((key) => localStorage.getItem(key) ?? '');
}

describe('auth session restore', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    sessionStorage.clear();
    useAuthStore.getState().clearSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    sessionStorage.clear();
  });

  it('restores an in-memory access token from the browser auth session without localStorage', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/authorize') {
        return Promise.resolve(authResponse(url.searchParams.get('state') ?? ''));
      }
      if (url.pathname === '/connect/token') {
        return Promise.resolve(tokenResponse());
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(restoreSessionFromBrowserAuth()).resolves.toBe(true);

    expect(getAccessToken()).toBe('restored-access-token');
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
    expect(localStorageValues()).not.toContain('restored-access-token');
  });

  it('lets the authenticated route continue after browser session restore', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/authorize') {
        return Promise.resolve(authResponse(url.searchParams.get('state') ?? ''));
      }
      if (url.pathname === '/connect/token') {
        return Promise.resolve(tokenResponse());
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(ensureAuthenticatedRouteSession()).resolves.toBeUndefined();
    expect(getAccessToken()).toBe('restored-access-token');
  });

  it('redirects the authenticated route when no browser auth session exists', async () => {
    vi.mocked(fetch).mockResolvedValue(unauthenticatedResponse());

    await expect(ensureAuthenticatedRouteSession()).rejects.toMatchObject({
      options: { to: '/sign-in' },
    });
    expect(getAccessToken()).toBeNull();
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
  });

  it('returns true immediately when an access token is already in memory', async () => {
    useAuthStore.getState().setSession('existing-token');

    await expect(restoreSessionFromBrowserAuth()).resolves.toBe(true);

    expect(fetch).not.toHaveBeenCalled();
    expect(getAccessToken()).toBe('existing-token');
  });

  it('returns false when the callback state does not match', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/authorize') {
        return Promise.resolve(authResponse('wrong-state'));
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(restoreSessionFromBrowserAuth()).resolves.toBe(false);

    expect(getAccessToken()).toBeNull();
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
  });
});
