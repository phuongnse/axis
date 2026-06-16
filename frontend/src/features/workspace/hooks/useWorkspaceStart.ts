import { useQuery } from '@tanstack/react-query';

import {
  canReadTeamAccountSettings,
  getCurrentTeamAccountSettings,
  getCurrentUserProfile,
  workspaceQueryKeys,
} from '../api';

export function useCurrentUserProfileQuery() {
  return useQuery({
    queryKey: workspaceQueryKeys.currentUser(),
    queryFn: getCurrentUserProfile,
  });
}

export function useCurrentTeamAccountSettingsQuery(enabled: boolean) {
  return useQuery({
    queryKey: workspaceQueryKeys.currentTeamAccountSettings(),
    queryFn: getCurrentTeamAccountSettings,
    enabled,
  });
}

export function useWorkspaceStart() {
  const profileQuery = useCurrentUserProfileQuery();
  const canReadSettings = canReadTeamAccountSettings(profileQuery.data);
  const teamAccountSettingsQuery = useCurrentTeamAccountSettingsQuery(canReadSettings);

  return {
    profileQuery,
    teamAccountSettingsQuery,
    canReadSettings,
    hasWorkspace: Boolean(profileQuery.data?.teamAccountId),
  };
}
