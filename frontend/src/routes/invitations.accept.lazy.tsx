import { createLazyFileRoute } from '@tanstack/react-router';
import { AcceptWorkspaceInvitationPage } from '@/features/memberships/components/AcceptWorkspaceInvitationPage';
import { publicRouteNavigation } from '@/lib/route-navigation';

export const routeNavigation = publicRouteNavigation({
  escapeTargets: ['/sign-in', '/register'],
});

export const Route = createLazyFileRoute('/invitations/accept')({
  component: AcceptWorkspaceInvitationPage,
});
