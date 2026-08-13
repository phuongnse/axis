import { createFileRoute } from '@tanstack/react-router';
import {
  workspaceInvitationsQueryOptions,
  workspaceProductBuildersQueryOptions,
} from '@/features/memberships';
import type { MyRouterContext } from '../__root';

export const Route = createFileRoute('/_authenticated/memberships')({
  loader: ({ context }) => loadMembershipsRoute(context),
});

export function loadMembershipsRoute({ queryClient }: MyRouterContext) {
  return Promise.all([
    queryClient.ensureQueryData(workspaceProductBuildersQueryOptions()),
    queryClient.ensureQueryData(workspaceInvitationsQueryOptions()),
  ]);
}
