import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useMemo, useRef } from 'react';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';

import { createRegisterIdempotencyKey, registerTeamAccount } from '@/features/auth/api';
import { useLegalVersions } from '@/features/auth/hooks/useLegalVersions';
import { saveRegistrationContext } from '@/features/auth/registration-context';
import {
  createRegisterTeamAccountSchema,
  type RegisterTeamAccountFormValues,
} from '@/features/auth/schemas/register-team-account-schema';
import type { RegisterValidationErrorData } from '@/features/auth/types';
import { ApiError } from '@/lib/api';

function pickFirstError(
  errors: Record<string, string[]> | undefined,
  ...keys: string[]
): string | undefined {
  if (!errors) return undefined;
  for (const key of keys) {
    const value = errors[key];
    if (value && value.length > 0) return value[0];
  }
  return undefined;
}

function applyTeamAccountValidationErrors(
  form: UseFormReturn<RegisterTeamAccountFormValues>,
  errorData: RegisterValidationErrorData,
): boolean {
  let hasMappedFieldError = false;

  const teamAccountNameError = pickFirstError(
    errorData.errors,
    'TeamAccountName',
    'teamAccountName',
  );
  const emailError = pickFirstError(errorData.errors, 'TeamContactEmail', 'teamContactEmail');
  const termsError = pickFirstError(
    errorData.errors,
    'AcceptedTermsVersion',
    'acceptedTermsVersion',
    'AcceptedPrivacyVersion',
    'acceptedPrivacyVersion',
  );

  const setFieldError = (field: FieldPath<RegisterTeamAccountFormValues>, message: string) => {
    form.setError(field, { type: 'server', message });
    hasMappedFieldError = true;
  };

  if (teamAccountNameError) {
    setFieldError('teamAccountName', teamAccountNameError);
  }
  if (emailError) {
    setFieldError('teamContactEmail', emailError);
  }
  if (termsError) {
    setFieldError('acceptedTerms', termsError);
  }

  return hasMappedFieldError;
}

export function useRegisterTeamAccount() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: legalVersions } = useLegalVersions();
  const idempotencyKeyRef = useRef(createRegisterIdempotencyKey());
  const registerSchema = useMemo(() => createRegisterTeamAccountSchema(t), [t]);
  const genericSubmitError = t('validation.genericSubmit');
  const form = useForm<RegisterTeamAccountFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      teamAccountName: '',
      teamContactEmail: '',
      acceptedTerms: false,
    },
    mode: 'onSubmit',
  });

  const mutation = useMutation({
    mutationFn: async (values: RegisterTeamAccountFormValues) => {
      if (!legalVersions) {
        throw new Error(t('validation.legalVersionsMissing'));
      }

      return registerTeamAccount(
        {
          teamAccountName: values.teamAccountName.trim(),
          teamContactEmail: values.teamContactEmail.trim(),
          acceptedTermsVersion: legalVersions.termsVersion,
          acceptedPrivacyVersion: legalVersions.privacyVersion,
          subscriptionPlanId: null,
        },
        idempotencyKeyRef.current,
      );
    },
    onSuccess: (_data, values) => {
      saveRegistrationContext({
        email: values.teamContactEmail.trim(),
        teamAccountName: values.teamAccountName.trim(),
      });
      form.reset();
      void navigate({ to: '/register/confirmation' });
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError && error.status < 500) {
        const errorData = error.data as RegisterValidationErrorData;
        const hasMappedFieldError = applyTeamAccountValidationErrors(form, errorData);
        if (!hasMappedFieldError) {
          form.setError('root', {
            type: 'server',
            message: errorData.message ?? errorData.title ?? genericSubmitError,
          });
        }
        return;
      }

      form.setError('root', { type: 'server', message: genericSubmitError });
    },
  });

  async function submit(values: RegisterTeamAccountFormValues) {
    form.clearErrors('root');
    try {
      await mutation.mutateAsync(values);
    } catch {
      // Field and submit errors are applied in mutation.onError.
    }
  }

  return {
    form,
    loading: mutation.isPending,
    submit,
    legalVersions,
  };
}
