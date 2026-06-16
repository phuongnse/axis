import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type CurrentUserProfile = components['schemas']['CurrentUserProfileDto'];
export type TenantSettings = components['schemas']['TenantSettingsDto'];
export type UsageStats = components['schemas']['UsageStatsDto'];

export const TENANT_SETTINGS_READ_PERMISSION = 'tenant:settings:read';

export const workspaceQueryKeys = {
  all: ['workspace'] as const,
  currentUser: () => [...workspaceQueryKeys.all, 'current-user'] as const,
  currentTenantSettings: () => [...workspaceQueryKeys.all, 'current-tenant-settings'] as const,
};

export async function getCurrentUserProfile(): Promise<CurrentUserProfile> {
  return fetchApi<CurrentUserProfile>('/users/me');
}

export async function getCurrentTenantSettings(): Promise<TenantSettings> {
  return fetchApi<TenantSettings>('/tenants/current/settings');
}

export function canReadTenantSettings(profile: CurrentUserProfile | undefined): boolean {
  return Boolean(
    profile?.tenantId && profile.permissions?.includes(TENANT_SETTINGS_READ_PERMISSION),
  );
}
