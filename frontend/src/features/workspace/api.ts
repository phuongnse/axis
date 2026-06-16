import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type CurrentUserProfile = components['schemas']['CurrentUserProfileDto'];
export type WorkspaceSettings = components['schemas']['WorkspaceSettingsDto'];
export type UsageStats = components['schemas']['UsageStatsDto'];

export const WORKSPACE_SETTINGS_READ_PERMISSION = 'workspace:settings:read';

export const workspaceQueryKeys = {
  all: ['workspace'] as const,
  currentUser: () => [...workspaceQueryKeys.all, 'current-user'] as const,
  currentWorkspaceSettings: () =>
    [...workspaceQueryKeys.all, 'current-workspace-settings'] as const,
};

export async function getCurrentUserProfile(): Promise<CurrentUserProfile> {
  return fetchApi<CurrentUserProfile>('/users/me');
}

export async function getCurrentWorkspaceSettings(): Promise<WorkspaceSettings> {
  return fetchApi<WorkspaceSettings>('/workspaces/current/settings');
}

export function canReadWorkspaceSettings(profile: CurrentUserProfile | undefined): boolean {
  return Boolean(
    profile?.workspaceId && profile.permissions?.includes(WORKSPACE_SETTINGS_READ_PERMISSION),
  );
}
