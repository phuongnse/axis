import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type {
  ChangeWorkspaceInvitationRequest,
  InviteWorkspaceMemberDto,
  InviteWorkspaceMemberRequest,
  WorkspaceInvitationLifecycleDto,
  WorkspaceInvitationPageDto,
} from '@/lib/api-generated';

export const workspaceInvitationKeys = {
  all: ['workspace-invitations'] as const,
  list: (page: number, pageSize: number) =>
    ['workspace-invitations', 'list', page, pageSize] as const,
};

export function workspaceInvitationsQueryOptions(page = 1, pageSize = 20) {
  return queryOptions({
    queryKey: workspaceInvitationKeys.list(page, pageSize),
    queryFn: () =>
      fetchApi<WorkspaceInvitationPageDto>(
        `/workspace-invitations?page=${page}&pageSize=${pageSize}`,
      ),
  });
}

export function inviteWorkspaceMember(
  request: InviteWorkspaceMemberRequest,
): Promise<InviteWorkspaceMemberDto> {
  return fetchApi('/workspace-invitations', {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function resendWorkspaceInvitation(
  invitationId: string,
  request: ChangeWorkspaceInvitationRequest,
): Promise<WorkspaceInvitationLifecycleDto> {
  return fetchApi(`/workspace-invitations/${encodeURIComponent(invitationId)}/resend`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function revokeWorkspaceInvitation(
  invitationId: string,
  request: ChangeWorkspaceInvitationRequest,
): Promise<WorkspaceInvitationLifecycleDto> {
  return fetchApi(`/workspace-invitations/${encodeURIComponent(invitationId)}/revoke`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}
