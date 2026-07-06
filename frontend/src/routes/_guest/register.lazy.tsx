import { createLazyFileRoute } from '@tanstack/react-router';

import { RegisterPage } from '@/features/auth/components/RegisterPage';
import { publicRouteNavigation } from '@/lib/route-navigation';

export const routeNavigation = publicRouteNavigation({
  escapeTargets: ['/sign-in'],
});

export const Route = createLazyFileRoute('/_guest/register')({
  component: RegisterPage,
});
