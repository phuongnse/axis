import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';

import { completePostSignInPkceFlow, signInUser } from '@/features/auth/api';
import { getProblemDetail } from '@/features/auth/problem-details';
import { type SignInFormValues, signInSchema } from '@/features/auth/schemas/sign-in-schema';
import type { SignInValidationErrorData } from '@/features/auth/types';
import { ApiError } from '@/lib/api';

type SignInErrorKind = 'verification_required' | 'rate_limited' | 'form';

function getFieldError(
  errors: Record<string, string[]> | undefined,
  key: string,
): string | undefined {
  return errors?.[key]?.[0];
}

function applySignInValidationErrors(
  form: UseFormReturn<SignInFormValues>,
  errorData: SignInValidationErrorData,
): boolean {
  let hasMappedFieldError = false;

  const setFieldError = (field: FieldPath<SignInFormValues>, message: string) => {
    form.setError(field, { type: 'server', message });
    hasMappedFieldError = true;
  };

  const emailError = getFieldError(errorData.errors, 'email');
  const passwordError = getFieldError(errorData.errors, 'password');
  if (emailError) {
    setFieldError('email', emailError);
  }
  if (passwordError) {
    setFieldError('password', passwordError);
  }

  return hasMappedFieldError;
}

function classifySignInError(error: ApiError): SignInErrorKind {
  if (error.status === 429) return 'rate_limited';

  const detail = getProblemDetail(error).toLowerCase();
  if (detail.includes('verification is required')) return 'verification_required';
  return 'form';
}

export function useSignIn() {
  const navigate = useNavigate();
  const genericSubmitError = 'Something went wrong, please try again';
  const form = useForm<SignInFormValues>({
    resolver: zodResolver(signInSchema),
    defaultValues: {
      email: '',
      password: '',
    },
    mode: 'onSubmit',
  });

  const mutation = useMutation({
    mutationFn: async (values: SignInFormValues) =>
      signInUser({
        email: values.email.trim(),
        password: values.password,
      }),
    onSuccess: async (data) => {
      if (data?.sessionEstablished) {
        try {
          await completePostSignInPkceFlow();
          return;
        } catch {
          void navigate({ to: '/dashboard' });
          return;
        }
      }

      void navigate({ to: '/sign-in' });
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError && error.status < 500) {
        const errorKind = classifySignInError(error);
        const detail = getProblemDetail(error);
        if (errorKind === 'verification_required') {
          form.setError('root', { type: 'server', message: detail });
          return;
        }
        if (errorKind === 'rate_limited') {
          form.setError('root', {
            type: 'server',
            message: detail || 'Please wait before trying again.',
          });
          return;
        }

        const errorData = error.data as SignInValidationErrorData;
        const hasMappedFieldError = applySignInValidationErrors(form, errorData);
        if (!hasMappedFieldError) {
          form.setError('root', {
            type: 'server',
            message: detail || errorData.message || errorData.title || genericSubmitError,
          });
        }
        return;
      }

      form.setError('root', { type: 'server', message: genericSubmitError });
    },
  });

  async function submit(values: SignInFormValues) {
    form.clearErrors('root');
    await mutation.mutateAsync(values).catch(() => undefined);
  }

  const error =
    mutation.error instanceof ApiError
      ? {
          kind: classifySignInError(mutation.error),
          detail: getProblemDetail(mutation.error),
        }
      : null;

  return {
    form,
    loading: mutation.isPending,
    submit,
    verificationEmail:
      error?.kind === 'verification_required' ? form.getValues('email').trim() : null,
    rateLimited: error?.kind === 'rate_limited',
  };
}
