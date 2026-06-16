import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { authKeys, getTeamAccountSlugPreview } from '@/features/auth/api';

const DEBOUNCE_MS = 300;

export function useSlugPreview(teamAccountName: string) {
  const [debouncedName, setDebouncedName] = useState(teamAccountName.trim());

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedName(teamAccountName.trim()), DEBOUNCE_MS);
    return () => window.clearTimeout(handle);
  }, [teamAccountName]);

  const query = useQuery({
    queryKey: authKeys.slugPreview(debouncedName),
    queryFn: () => getTeamAccountSlugPreview(debouncedName),
    enabled: debouncedName.length >= 2,
    staleTime: 30_000,
  });

  return {
    slug: query.data?.slug ?? null,
    loading: query.isFetching && debouncedName.length >= 2,
  };
}
