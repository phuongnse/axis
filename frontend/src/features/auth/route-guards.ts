import { redirect } from '@tanstack/react-router';

import { exchangeAuthorizationCode, restoreSessionFromBrowserAuth } from '@/features/auth/api';
import { getAccessToken } from '@/features/auth/auth-store';
import { clearPkceSession, loadPkceSession } from '@/features/auth/pkce';

interface RouteGuardContext {
  preload?: boolean;
}

export async function redirectAuthenticatedUserFromGuestRoute(context: RouteGuardContext = {}) {
  if (context.preload) {
    return;
  }

  if (getAccessToken()) {
    throw redirect({ to: '/dashboard', replace: true });
  }

  const restored = await restoreSessionFromBrowserAuth();
  if (restored) {
    throw redirect({ to: '/dashboard', replace: true });
  }
}

export async function redirectFromAppEntryRoute(context: RouteGuardContext = {}) {
  if (context.preload) {
    return;
  }

  if (getAccessToken()) {
    throw redirect({ to: '/dashboard', replace: true });
  }

  const restored = await restoreSessionFromBrowserAuth();
  throw redirect({ to: restored ? '/dashboard' : '/sign-in', replace: true });
}

export async function redirectFromCallbackRoute() {
  if (getAccessToken()) {
    throw redirect({ to: '/dashboard', replace: true });
  }

  const params = new URLSearchParams(window.location.search);
  if (params.get('error')) {
    clearPkceSession();
    return;
  }

  const code = params.get('code');
  const state = params.get('state');
  const pkce = loadPkceSession();

  if (!code || !pkce || state !== pkce.state) {
    clearPkceSession();
    return;
  }

  try {
    await exchangeAuthorizationCode(code);
  } catch {
    clearPkceSession();
    throw redirect({
      to: '/callback',
      search: { error: 'tokenFailed' },
      replace: true,
    });
  }

  clearPkceSession();
  throw redirect({ to: '/dashboard', replace: true });
}
