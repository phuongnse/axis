import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { authKeys, getOrganizationSlugPreview } from '@/features/auth/api';

const DEBOUNCE_MS = 300;

export function useSlugPreview(orgName: string) {
  const [debouncedName, setDebouncedName] = useState(orgName.trim());

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedName(orgName.trim()), DEBOUNCE_MS);
    return () => window.clearTimeout(handle);
  }, [orgName]);

  const query = useQuery({
    queryKey: authKeys.slugPreview(debouncedName),
    queryFn: () => getOrganizationSlugPreview(debouncedName),
    enabled: debouncedName.length >= 2,
    staleTime: 30_000,
  });

  return {
    slug: query.data?.slug ?? null,
    loading: query.isFetching && debouncedName.length >= 2,
  };
}
