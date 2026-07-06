import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import {
  completePostSignInPkceFlow,
  completePostVerifyPkceFlow,
  restoreSessionFromBrowserAuth,
} from '@/features/auth/api';
import { getAccessToken, useAuthStore } from '@/features/auth/auth-store';
import {
  redirectAuthenticatedUserFromGuestRoute,
  redirectFromAppEntryRoute,
  redirectFromCallbackRoute,
} from '@/features/auth/route-guards';
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

function tokenFailureResponse(): Response {
  return {
    ok: false,
    status: 400,
  } as unknown as Response;
}

function setWindowPath(path: string): void {
  window.history.pushState({}, '', path);
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
    setWindowPath('/');
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

  it('completes post-verification sign-in through browser session restore', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/authorize') {
        return Promise.resolve(authResponse(url.searchParams.get('state') ?? ''));
      }
      if (url.pathname === '/connect/token') {
        return Promise.resolve(tokenResponse('verified-access-token'));
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(completePostVerifyPkceFlow()).resolves.toBe(true);

    expect(getAccessToken()).toBe('verified-access-token');
  });

  it('completes post-sign-in through browser session restore without opening the callback route', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/authorize') {
        return Promise.resolve(authResponse(url.searchParams.get('state') ?? ''));
      }
      if (url.pathname === '/connect/token') {
        return Promise.resolve(tokenResponse('sign-in-access-token'));
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(completePostSignInPkceFlow()).resolves.toBe(true);

    expect(getAccessToken()).toBe('sign-in-access-token');
    expect(window.location.pathname).toBe('/');
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

  it('redirects public auth routes when an access token already exists in memory', async () => {
    useAuthStore.getState().setSession('existing-token');

    await expect(redirectAuthenticatedUserFromGuestRoute()).rejects.toMatchObject({
      options: { to: '/dashboard', replace: true },
    });

    expect(fetch).not.toHaveBeenCalled();
    expect(getAccessToken()).toBe('existing-token');
  });

  it('redirects public auth routes after restoring a browser auth session', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/authorize') {
        return Promise.resolve(authResponse(url.searchParams.get('state') ?? ''));
      }
      if (url.pathname === '/connect/token') {
        return Promise.resolve(tokenResponse('public-route-token'));
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(redirectAuthenticatedUserFromGuestRoute()).rejects.toMatchObject({
      options: { to: '/dashboard', replace: true },
    });

    expect(getAccessToken()).toBe('public-route-token');
  });

  it('lets public auth routes render when no browser auth session exists', async () => {
    vi.mocked(fetch).mockResolvedValue(unauthenticatedResponse());

    await expect(redirectAuthenticatedUserFromGuestRoute()).resolves.toBeUndefined();

    expect(getAccessToken()).toBeNull();
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
  });

  it('routes the app entry directly to the dashboard when an access token already exists in memory', async () => {
    useAuthStore.getState().setSession('existing-token');

    await expect(redirectFromAppEntryRoute()).rejects.toMatchObject({
      options: { to: '/dashboard', replace: true },
    });

    expect(fetch).not.toHaveBeenCalled();
    expect(getAccessToken()).toBe('existing-token');
  });

  it('routes the app entry directly to the dashboard after restoring a browser auth session', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/authorize') {
        return Promise.resolve(authResponse(url.searchParams.get('state') ?? ''));
      }
      if (url.pathname === '/connect/token') {
        return Promise.resolve(tokenResponse('entry-route-token'));
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(redirectFromAppEntryRoute()).rejects.toMatchObject({
      options: { to: '/dashboard', replace: true },
    });

    expect(getAccessToken()).toBe('entry-route-token');
  });

  it('routes the app entry directly to sign-in when no browser auth session exists', async () => {
    vi.mocked(fetch).mockResolvedValue(unauthenticatedResponse());

    await expect(redirectFromAppEntryRoute()).rejects.toMatchObject({
      options: { to: '/sign-in', replace: true },
    });

    expect(getAccessToken()).toBeNull();
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
  });

  it('routes the callback directly to the dashboard after exchanging a valid authorization code', async () => {
    sessionStorage.setItem('pkce_verifier', 'verifier');
    sessionStorage.setItem('pkce_state', 'state');
    setWindowPath('/callback?code=auth-code&state=state');
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/token') {
        return Promise.resolve(tokenResponse('callback-access-token'));
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(redirectFromCallbackRoute()).rejects.toMatchObject({
      options: { to: '/dashboard', replace: true },
    });

    expect(getAccessToken()).toBe('callback-access-token');
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
  });

  it('routes the callback directly to the dashboard when an access token already exists in memory', async () => {
    useAuthStore.getState().setSession('existing-token');
    sessionStorage.setItem('pkce_verifier', 'verifier');
    sessionStorage.setItem('pkce_state', 'state');
    setWindowPath('/callback?code=auth-code&state=state');

    await expect(redirectFromCallbackRoute()).rejects.toMatchObject({
      options: { to: '/dashboard', replace: true },
    });

    expect(fetch).not.toHaveBeenCalled();
    expect(getAccessToken()).toBe('existing-token');
  });

  it('lets the callback recovery page render when the callback state is invalid', async () => {
    setWindowPath('/callback?code=auth-code&state=wrong-state');

    await expect(redirectFromCallbackRoute()).resolves.toBeUndefined();

    expect(fetch).not.toHaveBeenCalled();
    expect(getAccessToken()).toBeNull();
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
  });

  it('clears PKCE state when the callback has an error query parameter', async () => {
    sessionStorage.setItem('pkce_verifier', 'verifier');
    sessionStorage.setItem('pkce_state', 'state');
    setWindowPath('/callback?error=access_denied');

    await expect(redirectFromCallbackRoute()).resolves.toBeUndefined();

    expect(fetch).not.toHaveBeenCalled();
    expect(getAccessToken()).toBeNull();
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
  });

  it('routes the callback to recovery when token exchange fails', async () => {
    sessionStorage.setItem('pkce_verifier', 'verifier');
    sessionStorage.setItem('pkce_state', 'state');
    setWindowPath('/callback?code=auth-code&state=state');
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/token') {
        return Promise.resolve(tokenFailureResponse());
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(redirectFromCallbackRoute()).rejects.toMatchObject({
      options: { to: '/callback', search: { error: 'tokenFailed' }, replace: true },
    });

    expect(getAccessToken()).toBeNull();
    expect(sessionStorage.getItem('pkce_verifier')).toBeNull();
    expect(sessionStorage.getItem('pkce_state')).toBeNull();
  });

  it('redirects the authenticated route when browser session restore fails unexpectedly', async () => {
    vi.mocked(fetch).mockRejectedValue(new Error('identity server unavailable'));

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

  it('returns false and skips network when PKCE setup fails', async () => {
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('storage unavailable');
    });

    await expect(restoreSessionFromBrowserAuth()).resolves.toBe(false);

    expect(fetch).not.toHaveBeenCalled();
    expect(getAccessToken()).toBeNull();
  });

  it('uses a timeout signal for the browser authorization restore request', async () => {
    vi.mocked(fetch).mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), window.location.origin);
      if (url.pathname === '/connect/authorize') {
        expect(init?.signal).toBeInstanceOf(AbortSignal);
        return Promise.resolve(unauthenticatedResponse());
      }
      return Promise.reject(new Error(`Unexpected fetch: ${url.toString()}`));
    });

    await expect(restoreSessionFromBrowserAuth()).resolves.toBe(false);

    expect(fetch).toHaveBeenCalledTimes(1);
  });
});
