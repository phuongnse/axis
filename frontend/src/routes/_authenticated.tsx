import { createFileRoute, isRedirect, Outlet, redirect, useRouter } from '@tanstack/react-router';
import { AppShell } from '@/components/shared/AppShell';
import { BrowserSessionUnavailableError, restoreBrowserSession } from '@/features/auth/api';
import { getBrowserSessionStatus } from '@/features/auth/auth-store';
import { SessionUnavailablePage } from '@/features/auth/components/SessionUnavailablePage';

export const Route = createFileRoute('/_authenticated')({
  beforeLoad: ensureAuthenticatedRouteSession,
  component: AuthenticatedLayout,
  errorComponent: AuthenticatedRouteError,
});

export async function ensureAuthenticatedRouteSession(context: { preload?: boolean } = {}) {
  if (context.preload) {
    return;
  }

  if (getBrowserSessionStatus() === 'authenticated') {
    return;
  }

  try {
    const restored = await restoreBrowserSession();
    if (!restored) {
      throw redirect({ to: '/sign-in' });
    }
  } catch (error) {
    if (isRedirect(error)) {
      throw error;
    }
    if (error instanceof BrowserSessionUnavailableError) throw error;
    throw new BrowserSessionUnavailableError();
  }
}

function AuthenticatedRouteError({ error, reset }: { error: Error; reset: () => void }) {
  const router = useRouter();
  if (!(error instanceof BrowserSessionUnavailableError)) throw error;

  return (
    <SessionUnavailablePage
      onRetry={() => {
        reset();
        void router.invalidate();
      }}
    />
  );
}

function AuthenticatedLayout() {
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
