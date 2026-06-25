import { fetchApi } from '@/lib/api';
import type { components } from '@/lib/api-types';

export type CurrentUserProfile = components['schemas']['CurrentUserProfileDto'];

export const dashboardQueryKeys = {
  all: ['dashboard'] as const,
  currentUser: () => [...dashboardQueryKeys.all, 'current-user'] as const,
};

export async function getCurrentUserProfile(): Promise<CurrentUserProfile> {
  return fetchApi<CurrentUserProfile>('/users/me');
}
