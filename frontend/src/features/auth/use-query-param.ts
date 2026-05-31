import { useLocation } from '@tanstack/react-router';
import { useMemo } from 'react';

export function useQueryParam(name: string): string | undefined {
  // `location.search` is the parsed search object in TanStack Router; use the
  // raw query string so URLSearchParams gets a string and the memo dep is stable.
  const { searchStr } = useLocation();
  return useMemo(() => new URLSearchParams(searchStr).get(name) ?? undefined, [searchStr, name]);
}
