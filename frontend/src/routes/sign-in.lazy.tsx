import { createLazyFileRoute } from '@tanstack/react-router';

import { SignInPage } from '@/features/auth/components/SignInPage';
import { publicRouteNavigation } from '@/lib/route-navigation';

export const routeNavigation = publicRouteNavigation({
  escapeTargets: ['/register'],
});

export const Route = createLazyFileRoute('/sign-in')({
  component: SignInPage,
});
