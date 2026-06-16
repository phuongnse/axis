import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';

import { authKeys, getWorkspaceSlugPreview } from '@/features/auth/api';

const DEBOUNCE_MS = 300;

export function useSlugPreview(workspaceName: string) {
  const [debouncedName, setDebouncedName] = useState(workspaceName.trim());

  useEffect(() => {
    const handle = window.setTimeout(() => setDebouncedName(workspaceName.trim()), DEBOUNCE_MS);
    return () => window.clearTimeout(handle);
  }, [workspaceName]);

  const query = useQuery({
    queryKey: authKeys.slugPreview(debouncedName),
    queryFn: () => getWorkspaceSlugPreview(debouncedName),
    enabled: debouncedName.length >= 2,
    staleTime: 30_000,
  });

  return {
    slug: query.data?.slug ?? null,
    loading: query.isFetching && debouncedName.length >= 2,
  };
}
