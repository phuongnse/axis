import { redirect } from '@tanstack/react-router';

import { BrowserSessionUnavailableError, restoreBrowserSession } from '@/features/auth/api';
import { getBrowserSessionStatus } from '@/features/auth/auth-store';
import { getAuthorizationRequestContinuation } from '@/features/auth/authorization-request';

interface RouteGuardContext {
  preload?: boolean;
}

function hasPendingAuthorizationRequest(): boolean {
  if (typeof window === 'undefined' || window.location.pathname !== '/sign-in') {
    return false;
  }

  const search = new URLSearchParams(window.location.search);
  return Boolean(
    getAuthorizationRequestContinuation(
      search.get('authorization_request') ?? undefined,
      search.get('authorization_client') ?? undefined,
    ),
  );
}

export async function redirectAuthenticatedUserFromGuestRoute(context: RouteGuardContext = {}) {
  if (context.preload) {
    return;
  }

  if (hasPendingAuthorizationRequest()) {
    return;
  }

  if (getBrowserSessionStatus() === 'authenticated') {
    throw redirect({ to: '/dashboard', replace: true });
  }

  let restored: boolean;
  try {
    restored = await restoreBrowserSession();
  } catch (error) {
    if (error instanceof BrowserSessionUnavailableError) return;
    throw error;
  }
  if (restored) {
    throw redirect({ to: '/dashboard', replace: true });
  }
}

export async function redirectFromAppEntryRoute(context: RouteGuardContext = {}) {
  if (context.preload) {
    return;
  }

  if (getBrowserSessionStatus() === 'authenticated') {
    throw redirect({ to: '/dashboard', replace: true });
  }

  const restored = await restoreBrowserSession();
  throw redirect({ to: restored ? '/dashboard' : '/sign-in', replace: true });
}
