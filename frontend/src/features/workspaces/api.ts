import { fetchApi } from '@/lib/api';
import type {
  CreateOrganizationWorkspaceDto,
  CreateOrganizationWorkspaceRequest,
} from '@/lib/api-generated';

export interface EligibleWorkspace {
  workspaceId: string;
  name: string;
  slug: string;
  type: 'Personal' | 'Organization';
  organizationId: string | null;
  isCurrent: boolean;
}

export interface WorkspaceContextTransition {
  transitionId: string | null;
  status: 'None' | 'Pending' | 'Completed' | 'Compensated' | 'Failed' | 'Expired';
  expiresAt: string | null;
  authoritativeWorkspaceId: string | null;
}

export interface CreatedOrganizationWorkspace {
  organizationName: string;
  workspaceId: string;
  workspaceName: string;
}

export const workspaceKeys = {
  all: ['workspaces'] as const,
  eligible: ['workspaces', 'eligible'] as const,
};

export async function listEligibleWorkspaces(): Promise<EligibleWorkspace[]> {
  return fetchApi<EligibleWorkspace[]>('/workspace-context/eligible');
}

export async function beginWorkspaceTransition(
  targetWorkspaceId: string,
): Promise<WorkspaceContextTransition> {
  return fetchApi<WorkspaceContextTransition>('/workspace-context/begin', {
    method: 'POST',
    body: JSON.stringify({ targetWorkspaceId }),
  });
}

export async function confirmWorkspaceTransition(): Promise<WorkspaceContextTransition> {
  return fetchApi<WorkspaceContextTransition>('/workspace-context/confirm', { method: 'POST' });
}

export async function recoverWorkspaceTransition(): Promise<WorkspaceContextTransition> {
  return fetchApi<WorkspaceContextTransition>('/workspace-context/recover', { method: 'POST' });
}

export async function createOrganizationWorkspace(
  request: CreateOrganizationWorkspaceRequest,
  idempotencyKey: string,
): Promise<CreatedOrganizationWorkspace> {
  const response = await fetchApi<CreateOrganizationWorkspaceDto>('/organizations', {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(request),
  });

  if (!response.organizationName || !response.workspaceId || !response.workspaceName) {
    throw new Error('Organization creation returned an incomplete result.');
  }

  return {
    organizationName: response.organizationName,
    workspaceId: response.workspaceId,
    workspaceName: response.workspaceName,
  };
}

export function createOrganizationIdempotencyKey(): string {
  return crypto.randomUUID();
}
