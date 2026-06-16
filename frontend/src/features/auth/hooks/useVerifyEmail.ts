import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';

import { completePostVerifyPkceFlow, verifyEmail } from '@/features/auth/api';
import { getProblemDetail } from '@/features/auth/problem-details';
import type { VerifyEmailErrorKind } from '@/features/auth/types';
import { ApiError } from '@/lib/api';

export function classifyVerifyEmailError(error: ApiError): VerifyEmailErrorKind {
  if (error.status === 429) return 'rate_limited';

  const detail = getProblemDetail(error).toLowerCase();
  if (detail.includes('expired')) return 'expired';
  if (detail.includes('already been used') || detail.includes('sign in')) return 'already_used';
  return 'invalid';
}

export function useVerifyEmail() {
  const navigate = useNavigate();

  const mutation = useMutation({
    mutationFn: verifyEmail,
    onSuccess: async (data, token) => {
      if (data?.nextStep === 'RegisterUser' && data.TenantSetupToken) {
        void navigate({
          to: '/register',
          search: { setupToken: data.TenantSetupToken },
        });
        return;
      }

      if (data?.sessionEstablished) {
        const shouldProvision = data.nextStep === 'WorkspaceProvisioning';
        try {
          await completePostVerifyPkceFlow(shouldProvision ? token : null);
          return;
        } catch {
          if (shouldProvision) {
            void navigate({
              to: '/provisioning',
              search: { token },
            });
            return;
          }
        }
      }

      void navigate({ to: '/login' });
    },
  });

  async function submit(token: string) {
    await mutation.mutateAsync(token);
  }

  const errorKind =
    mutation.error instanceof ApiError ? classifyVerifyEmailError(mutation.error) : null;

  return {
    submit,
    loading: mutation.isPending,
    errorKind,
    isSuccess: mutation.isSuccess,
  };
}
