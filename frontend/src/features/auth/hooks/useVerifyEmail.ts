import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useCallback } from 'react';

import { completePostVerifyPkceFlow, verifyEmail } from '@/features/auth/api';
import { classifyVerifyEmailError } from '@/features/auth/problem-details';
import { ApiError } from '@/lib/api';

export function useVerifyEmail() {
  const navigate = useNavigate();

  const mutation = useMutation({
    mutationFn: verifyEmail,
    onSuccess: (data) => {
      if (!data?.sessionEstablished) void navigate({ to: '/register' });
    },
  });

  const completeSignIn = useCallback(async () => {
    try {
      const completed = await completePostVerifyPkceFlow();
      if (completed) {
        void navigate({ to: '/dashboard', replace: true });
        return;
      }
    } catch {
      void navigate({ to: '/dashboard', replace: true });
    }
  }, [navigate]);

  async function submit(token: string) {
    await mutation.mutateAsync(token);
  }

  const errorKind =
    mutation.error instanceof ApiError ? classifyVerifyEmailError(mutation.error) : null;

  return {
    submit,
    completeSignIn,
    loading: mutation.isPending,
    errorKind,
    isSuccess: mutation.isSuccess,
    sessionEstablished: mutation.data?.sessionEstablished === true,
  };
}
