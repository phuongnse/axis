import { queryOptions } from '@tanstack/react-query';
import { fetchApi } from '@/lib/api';
import type { ModuleNavigationAvailabilityDto } from '@/lib/api-generated';

export const moduleNavigationAvailabilityKeys = {
  all: ['module-navigation-availability'] as const,
};

export function moduleNavigationAvailabilityQueryOptions() {
  return queryOptions({
    queryKey: moduleNavigationAvailabilityKeys.all,
    queryFn: getModuleNavigationAvailability,
  });
}

async function getModuleNavigationAvailability(): Promise<ModuleNavigationAvailabilityDto> {
  return fetchApi<ModuleNavigationAvailabilityDto>('/module-navigation');
}
