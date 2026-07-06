import { createFileRoute } from '@tanstack/react-router';

import { CallbackPage } from '@/features/auth/components/CallbackPage';
import { redirectFromCallbackRoute } from '@/features/auth/route-guards';
import { publicRouteNavigation } from '@/lib/route-navigation';

export const routeNavigation = publicRouteNavigation({
  escapeTargets: ['/sign-in'],
});

export const Route = createFileRoute('/callback')({
  beforeLoad: redirectFromCallbackRoute,
  validateSearch: (search: Record<string, unknown>): { error?: 'tokenFailed' } => ({
    error: search.error === 'tokenFailed' ? 'tokenFailed' : undefined,
  }),
  component: CallbackPage,
});
