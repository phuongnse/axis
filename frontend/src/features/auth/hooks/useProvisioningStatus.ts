import { useQuery } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useEffect } from 'react';

import { authKeys, getProvisioningStatus } from '@/features/auth/api';
import { getAccessToken } from '@/features/auth/auth-store';
import { deriveProvisioningUiState } from '@/features/auth/provisioning-steps';

const POLL_INTERVAL_MS = 5000;

export function useProvisioningStatus(token: string | undefined) {
  const navigate = useNavigate();

  const query = useQuery({
    queryKey: authKeys.provisioningStatus(token ?? ''),
    queryFn: () => getProvisioningStatus(token ?? ''),
    enabled: Boolean(token),
    refetchInterval: (currentQuery) => {
      const data = currentQuery.state.data;
      if (!data || data.isReady) return false;
      return POLL_INTERVAL_MS;
    },
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
