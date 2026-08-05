import { createFileRoute } from '@tanstack/react-router';

import { publicRouteNavigation } from '@/lib/route-navigation';

export const routeNavigation = publicRouteNavigation({
  escapeTargets: ['/register'],
});

export interface SignInRouteSearch {
  authorization_request?: string;
  authorization_client?: string;
}

export const Route = createFileRoute('/_guest/sign-in')({
  validateSearch: (search: Record<string, unknown>): SignInRouteSearch => ({
    authorization_request:
      typeof search.authorization_request === 'string' ? search.authorization_request : undefined,
    authorization_client:
      typeof search.authorization_client === 'string' ? search.authorization_client : undefined,
  }),
});
