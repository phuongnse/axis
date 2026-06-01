import { useMutation } from '@tanstack/react-query';

import { resendVerificationEmail } from '@/features/auth/api';
import type { ResendVerificationState } from '@/features/auth/types';
import { ApiError } from '@/lib/api';

export function useResendVerification() {
  const mutation = useMutation({
    mutationFn: resendVerificationEmail,
  });

  function resend(email: string) {
    return mutation.mutateAsync(email);
  }

  let state: ResendVerificationState = 'idle';
  if (mutation.isPending) {
    state = 'sending';
  } else if (mutation.isSuccess) {
    state = 'success';
  } else if (mutation.error instanceof ApiError && mutation.error.status === 429) {
    state = 'rate_limited';
  } else if (mutation.isError) {
    state = 'error';
  }

  const rateLimitMessage =
    mutation.error instanceof ApiError && mutation.error.status === 429
      ? getRateLimitMessage(mutation.error)
      : null;

  return {
    resend,
    state,
    rateLimitMessage,
    reset: mutation.reset,
  };
}

function getRateLimitMessage(error: ApiError): string {
  const data = error.data;
  if (
    typeof data === 'object' &&
    data !== null &&
    typeof (data as { detail?: string }).detail === 'string'
  ) {
    return (data as { detail: string }).detail;
  }
  return 'Please wait before requesting another email.';
}
