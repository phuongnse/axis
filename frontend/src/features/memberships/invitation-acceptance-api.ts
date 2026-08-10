import { restoreBrowserSession } from '@/features/auth/api';
import { useAuthStore } from '@/features/auth/auth-store';
import {
  beginWorkspaceTransition,
  confirmWorkspaceTransition,
  recoverWorkspaceTransition,
} from '@/features/workspaces/api';
import { ApiError, fetchApi, invalidateClientRequestSession } from '@/lib/api';
import { queryClient } from '@/lib/query-client';

export interface WorkspaceInvitationReview {
  invitationId: string;
  workspaceId: string;
  organizationName: string;
  workspaceName: string;
  inviterName: string;
  requestedRole: string;
  expiresAt: string;
}

export interface WorkspaceInvitationAcceptance {
  outcome: string;
  workspaceId: string;
  organizationRole: string;
  workspaceRole: string;
}

export async function awaitInvitationBootstrap(): Promise<boolean> {
  const outcome = await window.__axisInvitationBootstrap;
  if (outcome?.status === 'failed') return false;
  const state = await fetchApi<{ active: boolean }>('/internal/workspace-invitations/handoff');
  return state.active;
}

export function reviewWorkspaceInvitation(): Promise<WorkspaceInvitationReview> {
  return fetchApi('/internal/workspace-invitations/review');
}

export function acceptWorkspaceInvitation(): Promise<WorkspaceInvitationAcceptance> {
  return fetchApi('/internal/workspace-invitations/accept', { method: 'POST' });
}

export async function enterAcceptedWorkspace(workspaceId: string): Promise<void> {
  invalidateClientRequestSession();
  let confirmationAttempted = false;
  try {
    await beginWorkspaceTransition(workspaceId);
    confirmationAttempted = true;
    const completed = await confirmWorkspaceTransition();
    if (completed.status !== 'Completed' || completed.authoritativeWorkspaceId !== workspaceId) {
      throw new Error('Workspace transition did not complete.');
    }
  } catch (error) {
    if (!confirmationAttempted) throw error;
    const recovered = await recoverWorkspaceTransition();
    if (recovered.status !== 'Completed' || recovered.authoritativeWorkspaceId !== workspaceId) {
      throw error;
    }
  }

  queryClient.clear();
  useAuthStore.getState().clearSession();
  if (!(await restoreBrowserSession({ force: true }))) {
    throw new ApiError(401, null, 'The target Workspace session could not be restored.');
  }
}
