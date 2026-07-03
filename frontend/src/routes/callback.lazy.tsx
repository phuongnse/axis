import { createLazyFileRoute } from '@tanstack/react-router';

import { CallbackPage } from '@/features/auth/components/CallbackPage';
import { publicRouteNavigation } from '@/lib/route-navigation';

export const routeNavigation = publicRouteNavigation({
  escapeTargets: ['/sign-in'],
});

export const Route = createLazyFileRoute('/callback')({
  component: CallbackPage,
});
