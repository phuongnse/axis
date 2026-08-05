import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { completePostVerifyFlow, restoreBrowserSession, signInUser } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import { buildAuthorizationRequestResumeUrl } from '@/features/auth/authorization-request';
import {
  redirectAuthenticatedUserFromGuestRoute,
  redirectFromAppEntryRoute,
} from '@/features/auth/route-guards';
import { ensureAuthenticatedRouteSession } from '@/routes/_authenticated';

const authenticatedSession = {
  authenticated: true,
  csrfToken: 'authenticated-csrf',
  user: {
    userId: '11111111-1111-4111-8111-111111111111',
    workspaceId: '22222222-2222-4222-8222-222222222222',
    email: 'ada@example.com',
    name: 'Ada Lovelace',
  },
};

const guestSession = {
  authenticated: false,
  csrfToken: 'guest-csrf',
  user: undefined,
};

function jsonResponse(body: unknown, status = 200): Response {
  return {
    ok: status >= 200 && status < 300,
    status,
    statusText: status === 200 ? 'OK' : 'Error',
    json: () => Promise.resolve(body),
    text: () => Promise.resolve(JSON.stringify(body)),
  } as unknown as Response;
}

describe('browser session restore', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn());
    localStorage.clear();
    sessionStorage.clear();
    useAuthStore.getState().clearSession();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    window.history.pushState({}, '', '/');
  });

  it('restores the authenticated user and CSRF token from the same-origin session endpoint', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(authenticatedSession));

    await expect(restoreBrowserSession()).resolves.toBe(true);

    expect(useAuthStore.getState()).toMatchObject({
      browserSessionStatus: 'authenticated',
      csrfToken: 'authenticated-csrf',
      user: authenticatedSession.user,
      userLabel: 'Ada Lovelace',
      userInitials: 'AL',
    });
    expect(fetch).toHaveBeenCalledWith(
      '/api/auth/session',
      expect.objectContaining({ credentials: 'include' }),
    );
    expect(localStorage).toHaveLength(0);
    expect(sessionStorage).toHaveLength(0);
  });

  it('shares one guest resolution between app entry and the guest destination', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(guestSession));

    await expect(redirectFromAppEntryRoute()).rejects.toMatchObject({
      options: { to: '/sign-in', replace: true },
    });
    await expect(redirectAuthenticatedUserFromGuestRoute()).resolves.toBeUndefined();

    expect(fetch).toHaveBeenCalledTimes(1);
    expect(useAuthStore.getState().browserSessionStatus).toBe('guest');
  });

  it('lets protected routes continue when the opaque browser session is authenticated', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse(authenticatedSession));

    await expect(ensureAuthenticatedRouteSession()).resolves.toBeUndefined();

    expect(useAuthStore.getState().browserSessionStatus).toBe('authenticated');
  });

  it('redirects protected routes when session resolution fails closed', async () => {
    vi.mocked(fetch).mockRejectedValue(new Error('session service unavailable'));

    await expect(ensureAuthenticatedRouteSession()).rejects.toMatchObject({
      options: { to: '/sign-in' },
    });
    expect(useAuthStore.getState().browserSessionStatus).toBe('unknown');
  });

  it('force-refreshes a previously resolved guest after verification', async () => {
    useAuthStore.getState().setBrowserSession(guestSession);
    vi.mocked(fetch).mockResolvedValue(jsonResponse(authenticatedSession));

    await expect(completePostVerifyFlow()).resolves.toBe(true);

    expect(fetch).toHaveBeenCalledTimes(1);
    expect(useAuthStore.getState().browserSessionStatus).toBe('authenticated');
  });

  it('signs in with CSRF and refreshes identity-bound session state', async () => {
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse(guestSession))
      .mockResolvedValueOnce(jsonResponse({ sessionEstablished: true, nextStep: 'Dashboard' }))
      .mockResolvedValueOnce(jsonResponse(authenticatedSession));

    await expect(
      signInUser({ email: 'ada@example.com', password: 'correct horse battery staple' }),
    ).resolves.toMatchObject({ sessionEstablished: true });

    const calls = vi.mocked(fetch).mock.calls;
    expect(calls.map(([input]) => new URL(String(input), window.location.origin).pathname)).toEqual(
      ['/api/auth/session', '/api/auth/sign-in', '/api/auth/session'],
    );
    const signInHeaders = calls[1][1]?.headers as Headers;
    expect(signInHeaders.get('X-CSRF-TOKEN')).toBe('guest-csrf');
    expect(useAuthStore.getState().csrfToken).toBe('authenticated-csrf');
  });

  it('resumes an external authorization request with its public client ID and opaque request URI', () => {
    const url = new URL(
      buildAuthorizationRequestResumeUrl({
        clientId: 'local-mcp-client',
        requestUri: 'urn:ietf:params:oauth:request_uri:opaque',
      }),
    );

    expect(url.pathname).toBe('/connect/authorize');
    expect(url.searchParams.get('client_id')).toBe('local-mcp-client');
    expect(url.searchParams.get('request_uri')).toBe('urn:ietf:params:oauth:request_uri:opaque');
    expect([...url.searchParams.keys()]).toEqual(['client_id', 'request_uri']);
  });

  it('keeps route preloads free of session requests', async () => {
    const asPreloadGuard = (guard: unknown) =>
      (guard as (context: { preload: boolean }) => Promise<void>)({ preload: true });

    await expect(asPreloadGuard(redirectAuthenticatedUserFromGuestRoute)).resolves.toBeUndefined();
    await expect(asPreloadGuard(redirectFromAppEntryRoute)).resolves.toBeUndefined();
    await expect(asPreloadGuard(ensureAuthenticatedRouteSession)).resolves.toBeUndefined();
    expect(fetch).not.toHaveBeenCalled();
  });
});
