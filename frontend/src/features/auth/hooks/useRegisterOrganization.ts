import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useMemo, useRef } from 'react';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';

import { createRegisterIdempotencyKey, registerOrganization } from '@/features/auth/api';
import { useLegalVersions } from '@/features/auth/hooks/useLegalVersions';
import { saveRegistrationContext } from '@/features/auth/registration-context';
import {
  createRegisterOrganizationSchema,
  type RegisterOrganizationFormValues,
} from '@/features/auth/schemas/register-organization-schema';
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

function applyOrganizationValidationErrors(
  form: UseFormReturn<RegisterOrganizationFormValues>,
  errorData: RegisterValidationErrorData,
): boolean {
  let hasMappedFieldError = false;

  const orgNameError = pickFirstError(errorData.errors, 'OrgName', 'orgName');
  const emailError = pickFirstError(
    errorData.errors,
    'OrganizationContactEmail',
    'organizationContactEmail',
  );
  const termsError = pickFirstError(
    errorData.errors,
    'AcceptedTermsVersion',
    'acceptedTermsVersion',
    'AcceptedPrivacyVersion',
    'acceptedPrivacyVersion',
  );

  const setFieldError = (field: FieldPath<RegisterOrganizationFormValues>, message: string) => {
    form.setError(field, { type: 'server', message });
    hasMappedFieldError = true;
  };

  if (orgNameError) {
    setFieldError('orgName', orgNameError);
  }
  if (emailError) {
    setFieldError('organizationContactEmail', emailError);
  }
  if (termsError) {
    setFieldError('acceptedTerms', termsError);
  }

  return hasMappedFieldError;
}

export function useRegisterOrganization() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { data: legalVersions } = useLegalVersions();
  const idempotencyKeyRef = useRef(createRegisterIdempotencyKey());
  const registerSchema = useMemo(() => createRegisterOrganizationSchema(t), [t]);
  const genericSubmitError = t('validation.genericSubmit');
  const form = useForm<RegisterOrganizationFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      orgName: '',
      organizationContactEmail: '',
      acceptedTerms: false,
    },
    mode: 'onSubmit',
  });

  const mutation = useMutation({
    mutationFn: async (values: RegisterOrganizationFormValues) => {
      if (!legalVersions) {
        throw new Error(t('validation.legalVersionsMissing'));
      }

      return registerOrganization(
        {
          orgName: values.orgName.trim(),
          organizationContactEmail: values.organizationContactEmail.trim(),
          acceptedTermsVersion: legalVersions.termsVersion,
          acceptedPrivacyVersion: legalVersions.privacyVersion,
          subscriptionPlanId: null,
        },
        idempotencyKeyRef.current,
      );
    },
    onSuccess: (_data, values) => {
      saveRegistrationContext({
        email: values.organizationContactEmail.trim(),
        organizationName: values.orgName.trim(),
      });
      form.reset();
      void navigate({ to: '/register/confirmation' });
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError && error.status < 500) {
        const errorData = error.data as RegisterValidationErrorData;
        const hasMappedFieldError = applyOrganizationValidationErrors(form, errorData);
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

  async function submit(values: RegisterOrganizationFormValues) {
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
