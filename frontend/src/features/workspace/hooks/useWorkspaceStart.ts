import { useQuery } from '@tanstack/react-query';

import {
  canReadTenantSettings,
  getCurrentTenantSettings,
  getCurrentUserProfile,
  workspaceQueryKeys,
} from '../api';

export function useCurrentUserProfileQuery() {
  return useQuery({
    queryKey: workspaceQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
  });
}

export function useCurrentTenantSettingsQuery(enabled: boolean) {
  return useQuery({
    queryKey: workspaceQueryKeys.currentTenantSettings(),
    queryFn: getCurrentTenantSettings,
    enabled,
  });
}

export function useWorkspaceStart() {
  const profileQuery = useCurrentUserProfileQuery();
  const canReadSettings = canReadTenantSettings(profileQuery.data);
  const tenantSettingsQuery = useCurrentTenantSettingsQuery(canReadSettings);

  return {
    profileQuery,
    tenantSettingsQuery,
    canReadSettings,
    hasWorkspace: Boolean(profileQuery.data?.tenantId),
  };
}
