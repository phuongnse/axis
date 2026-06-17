import { useQuery } from '@tanstack/react-query';

import {
  canReadWorkspaceSettings,
  getCurrentUserProfile,
  getCurrentWorkspaceSettings,
  workspaceQueryKeys,
} from '../api';

export function useCurrentUserProfileQuery() {
  return useQuery({
    queryKey: workspaceQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
  });
}

export function useCurrentWorkspaceSettingsQuery(enabled: boolean) {
  return useQuery({
    queryKey: workspaceQueryKeys.currentWorkspaceSettings(),
    queryFn: getCurrentWorkspaceSettings,
    enabled,
  });
}

export function useWorkspaceStart() {
  const profileQuery = useCurrentUserProfileQuery();
  const canReadSettings = canReadWorkspaceSettings(profileQuery.data);
  const workspaceSettingsQuery = useCurrentWorkspaceSettingsQuery(canReadSettings);

  return {
    profileQuery,
    workspaceSettingsQuery,
    canReadSettings,
    hasWorkspace: Boolean(profileQuery.data?.workspaceId),
  };
}
