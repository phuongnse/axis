import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useMemo, useRef } from 'react';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';

import { createRegisterIdempotencyKey, registerUser, toAdminNameParts } from '@/features/auth/api';
import { useLegalVersions } from '@/features/auth/hooks/useLegalVersions';
import { saveRegistrationContext } from '@/features/auth/registration-context';
import {
  createRegisterSchema,
  type RegisterFormValues,
} from '@/features/auth/schemas/register-schema';
import type { RegisterValidationErrorData } from '@/features/auth/types';
import { useQueryParam } from '@/features/auth/use-query-param';
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

function applyRegisterValidationErrors(
  form: UseFormReturn<RegisterFormValues>,
  errorData: RegisterValidationErrorData,
): boolean {
  let hasMappedFieldError = false;

  const fullNameError = pickFirstError(
    errorData.errors,
    'FirstName',
    'firstName',
    'LastName',
    'lastName',
    'fullName',
  );
  const emailError = pickFirstError(errorData.errors, 'Email', 'email');
  const passwordError = pickFirstError(errorData.errors, 'Password', 'password');
  const passwordConfirmationError = pickFirstError(
    errorData.errors,
    'PasswordConfirmation',
    'passwordConfirmation',
  );
  const termsError = pickFirstError(
    errorData.errors,
    'AcceptedTermsVersion',
    'acceptedTermsVersion',
    'AcceptedPrivacyVersion',
    'acceptedPrivacyVersion',
  );

  const setFieldError = (field: FieldPath<RegisterFormValues>, message: string) => {
    form.setError(field, { type: 'server', message });
    hasMappedFieldError = true;
  };

  if (fullNameError) {
    setFieldError('fullName', fullNameError);
  }
  if (emailError) {
    setFieldError('email', emailError);
  }
  if (passwordError) {
    setFieldError('password', passwordError);
  }
  if (passwordConfirmationError) {
    setFieldError('passwordConfirmation', passwordConfirmationError);
  }
  if (termsError) {
    setFieldError('acceptedTerms', termsError);
  }

  return hasMappedFieldError;
}

export function useRegister() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const teamAccountSetupToken = useQueryParam('setupToken');
  const { data: legalVersions } = useLegalVersions();
  const idempotencyKeyRef = useRef(createRegisterIdempotencyKey());
  const registerSchema = useMemo(() => createRegisterSchema(t), [t]);
  const genericSubmitError = t('validation.genericSubmit');
  const form = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      fullName: '',
      email: '',
      password: '',
      passwordConfirmation: '',
      acceptedTerms: false,
    },
    mode: 'onSubmit',
  });

  const mutation = useMutation({
    mutationFn: async (values: RegisterFormValues) => {
      if (!legalVersions) {
        throw new Error(t('validation.legalVersionsMissing'));
      }

      const names = toAdminNameParts(values.fullName);
      return registerUser(
        {
          firstName: names.firstName,
          lastName: names.lastName,
          email: values.email.trim(),
          password: values.password,
          passwordConfirmation: values.passwordConfirmation,
          acceptedTermsVersion: legalVersions.termsVersion,
          acceptedPrivacyVersion: legalVersions.privacyVersion,
          teamAccountSetupToken,
        },
        idempotencyKeyRef.current,
      );
    },
    onSuccess: (_data, values) => {
      saveRegistrationContext({
        email: values.email.trim(),
      });
      form.reset();
      void navigate({ to: '/register/confirmation' });
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError && error.status < 500) {
        const errorData = error.data as RegisterValidationErrorData;
        const hasMappedFieldError = applyRegisterValidationErrors(form, errorData);
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

  async function submit(values: RegisterFormValues) {
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
