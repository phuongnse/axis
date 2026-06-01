import { useQuery } from '@tanstack/react-query';

import { authKeys, getLegalVersions } from '@/features/auth/api';

export function useLegalVersions() {
  return useQuery({
    queryKey: authKeys.legalVersions,
    queryFn: getLegalVersions,
    staleTime: 60 * 60 * 1000,
  });
}
