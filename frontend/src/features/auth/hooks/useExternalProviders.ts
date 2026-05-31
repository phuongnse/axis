import { useQuery } from '@tanstack/react-query';

import { fetchExternalProviders } from '@/features/auth/api';

export function useExternalProviders() {
  return useQuery({
    queryKey: ['auth', 'external-providers'],
    queryFn: fetchExternalProviders,
    staleTime: 60_000,
  });
}
