import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useMemo, useState } from 'react';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { completePostSignInPkceFlow, signInUser } from '@/features/auth/api';
import { useRefreshClientValidationErrors } from '@/features/auth/hooks/useRefreshClientValidationErrors';
import {
  classifySignInError,
  getFirstFieldError,
  getSignInCopyKey,
  getSignInFieldError,
  type SignInSubmitErrorKind,
} from '@/features/auth/problem-details';
import { createSignInSchema, type SignInFormValues } from '@/features/auth/schemas/sign-in-schema';
import type { SignInValidationErrorData } from '@/features/auth/types';
import { currentSiteLanguage } from '@/features/preferences';
import type { TranslationKey } from '@/features/preferences/translations';
import { ApiError } from '@/lib/api';

const clientValidationFields: FieldPath<SignInFormValues>[] = ['email', 'password'];

function applySignInValidationErrors(
  form: UseFormReturn<SignInFormValues>,
  errorData: SignInValidationErrorData,
  t: ReturnType<typeof useTranslation>['t'],
): boolean {
  let hasMappedFieldError = false;

  const setFieldError = (
    field: FieldPath<SignInFormValues>,
    message: string,
    key?: TranslationKey,
  ) => {
    form.setError(field, { type: 'server', message: key ? t(key) : message });
    hasMappedFieldError = true;
  };

  const emailError = getFirstFieldError(errorData, 'email');
  const passwordError = getFirstFieldError(errorData, 'password');
  if (emailError) {
    setFieldError('email', emailError, getSignInFieldError(errorData, 'email')?.key);
  }
  if (passwordError) {
    setFieldError('password', passwordError, getSignInFieldError(errorData, 'password')?.key);
  }

  return hasMappedFieldError;
}

function getSignInFormErrorMessage(
  errorKind: SignInSubmitErrorKind,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  return t(getSignInCopyKey(errorKind));
}

export function useSignIn() {
  const navigate = useNavigate();
  const { t } = useTranslation();
  const language = currentSiteLanguage();
  const [submitErrorKind, setSubmitErrorKind] = useState<SignInSubmitErrorKind | null>(null);
  const [dashboardHandoffPending, setDashboardHandoffPending] = useState(false);
  const signInSchema = useMemo(
    () =>
      createSignInSchema({
        emailRequired: t('validation.emailRequired'),
        emailInvalid: t('validation.emailInvalid'),
        passwordRequired: t('validation.passwordRequired'),
      }),
    [t],
  );
  const form = useForm<SignInFormValues>({
    resolver: zodResolver(signInSchema),
    defaultValues: {
      email: '',
      password: '',
    },
    mode: 'onSubmit',
  });
  useRefreshClientValidationErrors(form, clientValidationFields, language);

  const mutation = useMutation({
    mutationFn: async (values: SignInFormValues) =>
      signInUser({
        email: values.email.trim(),
        password: values.password,
      }),
    onSuccess: async (data) => {
      if (data?.sessionEstablished) {
        setDashboardHandoffPending(true);
        try {
          const completed = await completePostSignInPkceFlow();
          if (completed) {
            await navigate({ to: '/dashboard', replace: true });
          }
          return;
        } catch {
          await navigate({ to: '/dashboard' });
          return;
        }
      }

      await navigate({ to: '/sign-in' });
    },
    onError: (error: unknown) => {
      setDashboardHandoffPending(false);
      if (error instanceof ApiError && error.status < 500) {
        const errorKind = classifySignInError(error);
        if (
          errorKind === 'verification_required' ||
          errorKind === 'rate_limited' ||
          errorKind === 'workspace_unavailable'
        ) {
          setSubmitErrorKind(errorKind);
          return;
        }

        const errorData = error.data as SignInValidationErrorData;
        const hasMappedFieldError = applySignInValidationErrors(form, errorData, t);
        if (!hasMappedFieldError) {
          setSubmitErrorKind(errorKind);
        }
        return;
      }

      setSubmitErrorKind('generic');
    },
  });

  async function submit(values: SignInFormValues) {
    setDashboardHandoffPending(false);
    setSubmitErrorKind(null);
    form.clearErrors();
    await mutation.mutateAsync(values).catch(() => undefined);
  }

  return {
    form,
    loading: mutation.isPending || dashboardHandoffPending,
    submit,
    submitError: submitErrorKind ? getSignInFormErrorMessage(submitErrorKind, t) : null,
    verificationEmail:
      submitErrorKind === 'verification_required' ? form.getValues('email').trim() : null,
    rateLimited: submitErrorKind === 'rate_limited',
  };
}
