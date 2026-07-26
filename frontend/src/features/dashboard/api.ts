import { fetchApi } from '@/lib/api';
import type * as ApiTypes from '@/lib/api-generated';

export type CurrentUserProfile = ApiTypes.CurrentUserProfileDto;

export const dashboardQueryKeys = {
  all: ['dashboard'] as const,
  currentUser: () => [...dashboardQueryKeys.all, 'current-user'] as const,
};

export async function getCurrentUserProfile(): Promise<CurrentUserProfile> {
  return fetchApi<CurrentUserProfile>('/users/me');
}
