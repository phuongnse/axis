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
      // `data` may be null/empty if the session was not established; only run the
      // PKCE hand-off when the API confirms it. Field is snake_case from the API.
      if (data?.session_established) {
        try {
          await completePostVerifyPkceFlow(token);
          return;
        } catch {
          // PKCE setup failed — fall back to the provisioning poll so the user
          // is not stranded on the "Redirecting…" screen.
        }
      }

      void navigate({
        to: '/provisioning',
        search: { token },
      });
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
