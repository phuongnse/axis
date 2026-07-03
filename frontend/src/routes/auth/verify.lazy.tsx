import { createLazyFileRoute } from '@tanstack/react-router';

import { VerifyEmailPage } from '@/features/auth/components/VerifyEmailPage';
import { publicRouteNavigation } from '@/lib/route-navigation';

export const routeNavigation = publicRouteNavigation({
  escapeTargets: ['/sign-in', '/register'],
});

export const Route = createLazyFileRoute('/auth/verify')({
  component: VerifyEmailPage,
});
