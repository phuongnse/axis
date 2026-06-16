import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type CurrentUserProfile = components['schemas']['CurrentUserProfileDto'];
export type OrganizationSettings = components['schemas']['OrganizationSettingsDto'];
export type UsageStats = components['schemas']['UsageStatsDto'];

export const ORGANIZATION_SETTINGS_READ_PERMISSION = 'organization:settings:read';

export const workspaceQueryKeys = {
  all: ['workspace'] as const,
  currentUser: () => [...workspaceQueryKeys.all, 'current-user'] as const,
  currentOrganizationSettings: () =>
    [...workspaceQueryKeys.all, 'current-organization-settings'] as const,
};

export async function getCurrentUserProfile(): Promise<CurrentUserProfile> {
  return fetchApi<CurrentUserProfile>('/users/me');
}

export async function getCurrentOrganizationSettings(): Promise<OrganizationSettings> {
  return fetchApi<OrganizationSettings>('/organizations/current/settings');
}

export function canReadOrganizationSettings(profile: CurrentUserProfile | undefined): boolean {
  return Boolean(
    profile?.orgId && profile.permissions?.includes(ORGANIZATION_SETTINGS_READ_PERMISSION),
  );
}
