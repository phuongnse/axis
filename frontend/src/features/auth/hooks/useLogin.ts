import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import type { TFunction } from 'i18next';
import { useMemo } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';

import { LoginRequestError, loginWithPassword } from '@/features/auth/api';
import { createLoginSchema, type LoginFormValues } from '@/features/auth/schemas/login-schema';

export type LoginErrorKind = 'credentials' | 'unverified' | 'deactivated' | 'locked' | 'server';

export interface LoginError {
  kind: LoginErrorKind;
  message: string;
}

function mapLoginFailure(status: number, bodyText: string, t: TFunction): LoginError {
  const lower = bodyText.toLowerCase();
  if (lower.includes('verify') || lower.includes('unverified'))
    return {
      kind: 'unverified',
      message: t('login.errors.unverified'),
    };
  if (lower.includes('deactivated'))
    return {
      kind: 'deactivated',
      message: t('login.errors.deactivated'),
    };
  if (lower.includes('too many') || lower.includes('locked'))
    return {
      kind: 'locked',
      message: t('login.errors.locked'),
    };
  if (status === 401) return { kind: 'credentials', message: t('login.errors.credentials') };
  return { kind: 'server', message: t('login.errors.server') };
}

export function useLogin() {
  const { t } = useTranslation();
  const loginSchema = useMemo(() => createLoginSchema(t), [t]);
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
        const mapped = mapLoginFailure(error.status, error.bodyText, t);
        form.setError('root', { type: 'server', message: mapped.message });
        if (mapped.kind === 'server') {
          form.setValue('password', '');
        }
        return;
      }

      form.setError('root', { type: 'server', message: t('login.errors.server') });
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
      ? mapLoginFailure(mutation.error.status, mutation.error.bodyText, t)
      : form.formState.errors.root?.message
        ? { kind: 'server', message: form.formState.errors.root.message }
        : null;

  return { form, loginError, loading: mutation.isPending, submit };
}
