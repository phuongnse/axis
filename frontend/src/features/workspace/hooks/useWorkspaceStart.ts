import { useQuery } from '@tanstack/react-query';

import {
  canReadOrganizationSettings,
  getCurrentOrganizationSettings,
  getCurrentUserProfile,
  workspaceQueryKeys,
} from '../api';

export function useCurrentUserProfileQuery() {
  return useQuery({
    queryKey: workspaceQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
  });
}

export function useCurrentOrganizationSettingsQuery(enabled: boolean) {
  return useQuery({
    queryKey: workspaceQueryKeys.currentOrganizationSettings(),
    queryFn: getCurrentOrganizationSettings,
    enabled,
  });
}

export function useWorkspaceStart() {
  const profileQuery = useCurrentUserProfileQuery();
  const canReadSettings = canReadOrganizationSettings(profileQuery.data);
  const organizationSettingsQuery = useCurrentOrganizationSettingsQuery(canReadSettings);

  return {
    profileQuery,
    organizationSettingsQuery,
    canReadSettings,
    hasWorkspace: Boolean(profileQuery.data?.orgId),
  };
}
