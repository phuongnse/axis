import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useEffect } from 'react';

import { authKeys, getProvisioningStatus } from '@/features/auth/api';
import { getAccessToken } from '@/features/auth/auth-store';
import { deriveProvisioningUiState } from '@/features/auth/provisioning-steps';
import type { ProvisioningStatusResponse } from '@/features/auth/types';
import { ApiError } from '@/lib/api';

const POLL_INTERVAL_MS = 5000;

/**
 * Decides the next poll interval from the latest query state.
 * - Stop once the workspace is ready.
 * - Stop on a terminal 4xx (e.g. invalid/expired token) — retrying cannot help.
 * - Otherwise keep polling, so a transient network blip or 5xx does not
 *   permanently freeze the screen (the previous `!data` guard stopped forever).
 */
export function resolveProvisioningPollInterval(
  data: ProvisioningStatusResponse | undefined,
  error: unknown,
): number | false {
  if (data?.isReady) return false;
  if (error instanceof ApiError && error.status >= 400 && error.status < 500) return false;
  return POLL_INTERVAL_MS;
}

export function useProvisioningStatus(token: string | undefined) {
  const navigate = useNavigate();

  const query = useQuery({
    queryKey: authKeys.provisioningStatus(token ?? ''),
    queryFn: () => getProvisioningStatus(token ?? ''),
    enabled: Boolean(token),
    refetchInterval: (currentQuery) =>
      resolveProvisioningPollInterval(currentQuery.state.data, currentQuery.state.error),
  });

  const uiState = query.data ? deriveProvisioningUiState(query.data) : null;

  useEffect(() => {
    if (!query.data?.isReady) return;

    const timeoutId = window.setTimeout(() => {
      if (getAccessToken()) {
        void navigate({ to: '/dashboard' });
        return;
      }
      void navigate({ to: '/login' });
    }, 800);

    return () => window.clearTimeout(timeoutId);
  }, [query.data?.isReady, navigate]);

  return {
    status: query.data ?? null,
    uiState,
    loading: query.isLoading,
    error: query.error,
    refetch: query.refetch,
  };
}
