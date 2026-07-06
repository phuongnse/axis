import { createLazyFileRoute } from '@tanstack/react-router';

import { EmailConfirmationPage } from '@/features/auth/components/EmailConfirmationPage';
import { publicRouteNavigation } from '@/lib/route-navigation';

export const routeNavigation = publicRouteNavigation({
  escapeTargets: ['/register'],
});

export const Route = createLazyFileRoute('/_guest/register_/confirmation')({
  component: EmailConfirmationPage,
});
