import { useLocation } from '@tanstack/react-router';
import { useMemo } from 'react';

export function useQueryParam(name: string): string | undefined {
  const location = useLocation();
  return useMemo(
    () => new URLSearchParams(location.search).get(name) ?? undefined,
    [location.search, name],
  );
}
