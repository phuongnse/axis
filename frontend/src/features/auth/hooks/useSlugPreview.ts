import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { authKeys, getTenantSlugPreview } from '@/features/auth/api';

const DEBOUNCE_MS = 300;

export function useSlugPreview(tenantName: string) {
  const [debouncedName, setDebouncedName] = useState(tenantName.trim());

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedName(tenantName.trim()), DEBOUNCE_MS);
    return () => window.clearTimeout(handle);
  }, [tenantName]);

  const query = useQuery({
    queryKey: authKeys.slugPreview(debouncedName),
    queryFn: () => getTenantSlugPreview(debouncedName),
    enabled: debouncedName.length >= 2,
    staleTime: 30_000,
  });

  return {
    slug: query.data?.slug ?? null,
    loading: query.isFetching && debouncedName.length >= 2,
  };
}
