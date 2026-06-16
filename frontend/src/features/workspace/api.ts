import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type CurrentUserProfile = components['schemas']['CurrentUserProfileDto'];
export type TeamAccountSettings = components['schemas']['TeamAccountSettingsDto'];
export type UsageStats = components['schemas']['UsageStatsDto'];

export const TEAM_ACCOUNT_SETTINGS_READ_PERMISSION = 'team-account:settings:read';

export const workspaceQueryKeys = {
  all: ['workspace'] as const,
  currentUser: () => [...workspaceQueryKeys.all, 'current-user'] as const,
  currentTeamAccountSettings: () =>
    [...workspaceQueryKeys.all, 'current-team-account-settings'] as const,
};

export async function getCurrentUserProfile(): Promise<CurrentUserProfile> {
  return fetchApi<CurrentUserProfile>('/users/me');
}

export async function getCurrentTeamAccountSettings(): Promise<TeamAccountSettings> {
  return fetchApi<TeamAccountSettings>('/team-accounts/current/settings');
}

export function canReadTeamAccountSettings(profile: CurrentUserProfile | undefined): boolean {
  return Boolean(
    profile?.teamAccountId && profile.permissions?.includes(TEAM_ACCOUNT_SETTINGS_READ_PERMISSION),
  );
}
