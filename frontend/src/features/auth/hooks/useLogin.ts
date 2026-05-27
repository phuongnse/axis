import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';

import { LoginRequestError, loginWithPassword } from '@/features/auth/api';
import { type LoginFormValues, loginSchema } from '@/features/auth/schemas/login-schema';

export type LoginErrorKind = 'credentials' | 'unverified' | 'deactivated' | 'locked' | 'server';

export interface LoginError {
  kind: LoginErrorKind;
  message: string;
}

function mapLoginFailure(status: number, bodyText: string): LoginError {
  const lower = bodyText.toLowerCase();
  if (lower.includes('verify') || lower.includes('unverified'))
    return {
      kind: 'unverified',
      message: 'Please verify your email before signing in.',
    };
  if (lower.includes('deactivated'))
    return {
      kind: 'deactivated',
      message: 'Your account has been deactivated. Contact your organization admin.',
    };
  if (lower.includes('too many') || lower.includes('locked'))
    return {
      kind: 'locked',
      message: 'Too many failed attempts. Try again later.',
    };
  if (status === 401) return { kind: 'credentials', message: 'Incorrect email or password' };
  return { kind: 'server', message: 'Something went wrong. Please try again.' };
}

export function useLogin() {
  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
    mode: 'onSubmit',
  });

  const mutation = useMutation({
    mutationFn: loginWithPassword,
    onSuccess: (result) => {
      if (result.location) {
        window.location.href = result.location.startsWith('http')
          ? result.location
          : `${window.location.origin}${result.location}`;
        return;
      }
      window.location.href = result.authorizeUrl;
    },
    onError: (error: unknown) => {
      if (error instanceof LoginRequestError) {
        const mapped = mapLoginFailure(error.status, error.bodyText);
        form.setError('root', { type: 'server', message: mapped.message });
        if (mapped.kind === 'server') {
          form.setValue('password', '');
        }
        return;
      }

      form.setError('root', { type: 'server', message: 'Something went wrong. Please try again.' });
      form.setValue('password', '');
    },
  });

  async function submit(values: LoginFormValues) {
    form.clearErrors('root');
    try {
      await mutation.mutateAsync(values);
    } catch {
      // Errors are handled in mutation.onError.
    }
  }

  const loginError =
    mutation.error instanceof LoginRequestError
      ? mapLoginFailure(mutation.error.status, mutation.error.bodyText)
      : form.formState.errors.root?.message
        ? { kind: 'server', message: form.formState.errors.root.message }
        : null;

  return { form, loginError, loading: mutation.isPending, submit };
}
