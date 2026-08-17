import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type {
  ChangeWorkspaceInvitationRequest,
  ChangeWorkspaceProductBuilderRequest,
  CollectionSortDirection,
  InviteWorkspaceMemberDto,
  InviteWorkspaceMemberRequest,
  WorkspaceInvitationLifecycleDto,
  WorkspaceInvitationPageDto,
  WorkspaceInvitationSortField,
  WorkspaceProductBuilderDto,
} from '@/lib/api-generated';

export const workspaceInvitationKeys = {
  all: ['workspace-invitations'] as const,
  list: (
    page: number,
    pageSize: number,
    sortBy?: WorkspaceInvitationSortField,
    sortDirection?: CollectionSortDirection,
  ) => ['workspace-invitations', 'list', page, pageSize, sortBy, sortDirection] as const,
};

export const workspaceProductBuilderKeys = {
  all: ['workspace-product-builders'] as const,
};

export function workspaceProductBuildersQueryOptions() {
  return queryOptions({
    queryKey: workspaceProductBuilderKeys.all,
    queryFn: () => fetchApi<WorkspaceProductBuilderDto[]>('/workspace-product-builders'),
  });
}

export function grantWorkspaceProductBuilder(
  userId: string,
  request: ChangeWorkspaceProductBuilderRequest,
): Promise<WorkspaceProductBuilderDto> {
  return fetchApi(`/workspace-product-builders/${encodeURIComponent(userId)}/grant`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function revokeWorkspaceProductBuilder(
  userId: string,
  request: ChangeWorkspaceProductBuilderRequest,
): Promise<WorkspaceProductBuilderDto> {
  return fetchApi(`/workspace-product-builders/${encodeURIComponent(userId)}/revoke`, {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function workspaceInvitationsQueryOptions(
  page = 1,
  pageSize = 20,
  sortBy?: WorkspaceInvitationSortField,
  sortDirection?: CollectionSortDirection,
) {
  const search = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (sortBy && sortDirection) {
    search.set('sortBy', sortBy);
    search.set('sortDirection', sortDirection);
  }
  return queryOptions({
    queryKey: workspaceInvitationKeys.list(page, pageSize, sortBy, sortDirection),
    queryFn: () => fetchApi<WorkspaceInvitationPageDto>(`/workspace-invitations?${search}`),
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
