import { createFileRoute, isRedirect, Outlet, redirect } from '@tanstack/react-router';
import { AppShell } from '@/components/shared/AppShell';
import { restoreBrowserSession } from '@/features/auth/api';
import { getBrowserSessionStatus } from '@/features/auth/auth-store';

export const Route = createFileRoute('/_authenticated')({
  beforeLoad: ensureAuthenticatedRouteSession,
  component: AuthenticatedLayout,
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
    throw redirect({ to: '/sign-in' });
  }
}

function AuthenticatedLayout() {
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
