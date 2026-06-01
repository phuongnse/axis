import { useMutation, useQueryClient } from '@tanstack/react-query';

import { authKeys, retryProvisioning } from '@/features/auth/api';

export function useRetryProvisioning(token: string | undefined) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async () => {
      if (!token) throw new Error('Missing provisioning token');
      await retryProvisioning(token);
    },
    onSuccess: async () => {
      if (token) {
        await queryClient.invalidateQueries({ queryKey: authKeys.provisioningStatus(token) });
      }
    },
  });
}
