import { createFileRoute, Outlet, redirect } from '@tanstack/react-router';
import { AppShell } from '@/components/shared/AppShell';
import { restoreSessionFromBrowserAuth } from '@/features/auth/api';
import { getAccessToken } from '@/features/auth/auth-store';

export const Route = createFileRoute('/_authenticated')({
  beforeLoad: ensureAuthenticatedRouteSession,
  component: AuthenticatedLayout,
});

export async function ensureAuthenticatedRouteSession() {
  if (getAccessToken()) {
    return;
  }

  const restored = await restoreSessionFromBrowserAuth();
  if (!restored) {
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
