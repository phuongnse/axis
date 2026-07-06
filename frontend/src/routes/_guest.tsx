import { createFileRoute, Outlet } from '@tanstack/react-router';

import { redirectAuthenticatedUserFromGuestRoute } from '@/features/auth/route-guards';

export const Route = createFileRoute('/_guest')({
  beforeLoad: redirectAuthenticatedUserFromGuestRoute,
  component: Outlet,
});
