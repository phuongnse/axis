import { createFileRoute, Outlet, redirect } from '@tanstack/react-router';
import { AppShell } from '@/components/shared/AppShell';
import { getAccessToken } from '@/features/auth/auth-store';

export const Route = createFileRoute('/_authenticated')({
  beforeLoad: () => {
    if (!getAccessToken()) {
      throw redirect({ to: '/register' });
    }
  },
  component: AuthenticatedLayout,
});

function AuthenticatedLayout() {
  return (
    <AppShell>
      <Outlet />
    </AppShell>
  );
}
