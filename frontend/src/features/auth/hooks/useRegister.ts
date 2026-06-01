import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useRef } from 'react';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';

import {
  createRegisterIdempotencyKey,
  registerOrganization,
  toAdminNameParts,
} from '@/features/auth/api';
import { useLegalVersions } from '@/features/auth/hooks/useLegalVersions';
import { saveRegistrationContext } from '@/features/auth/registration-context';
import { type RegisterFormValues, registerSchema } from '@/features/auth/schemas/register-schema';
import type { RegisterValidationErrorData } from '@/features/auth/types';
import { ApiError } from '@/lib/api';

const GENERIC_SUBMIT_ERROR = 'Something went wrong, please try again';

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

  const organizationNameError = pickFirstError(
    errorData.errors,
    'OrgName',
    'org_name',
    'organizationName',
  );
  const fullNameError = pickFirstError(
    errorData.errors,
    'AdminFirstName',
    'admin_first_name',
    'AdminLastName',
    'admin_last_name',
    'fullName',
  );
  const emailError = pickFirstError(errorData.errors, 'AdminEmail', 'admin_email', 'email');
  const passwordError = pickFirstError(errorData.errors, 'Password', 'password');
  const passwordConfirmationError = pickFirstError(
    errorData.errors,
    'PasswordConfirmation',
    'password_confirmation',
  );
  const termsError = pickFirstError(
    errorData.errors,
    'AcceptedTermsVersion',
    'accepted_terms_version',
    'AcceptedPrivacyVersion',
    'accepted_privacy_version',
  );

  const setFieldError = (field: FieldPath<RegisterFormValues>, message: string) => {
    form.setError(field, { type: 'server', message });
    hasMappedFieldError = true;
  };

  if (organizationNameError) {
    setFieldError('organizationName', organizationNameError);
  }
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
  const navigate = useNavigate();
  const { data: legalVersions } = useLegalVersions();
  const idempotencyKeyRef = useRef(createRegisterIdempotencyKey());
  const form = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      organizationName: '',
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
        throw new Error('Legal document versions are not loaded yet.');
      }

      const names = toAdminNameParts(values.fullName);
      return registerOrganization(
        {
          org_name: values.organizationName.trim(),
          admin_first_name: names.firstName,
          admin_last_name: names.lastName,
          admin_email: values.email.trim(),
          password: values.password,
          password_confirmation: values.passwordConfirmation,
          accepted_terms_version: legalVersions.terms_version,
          accepted_privacy_version: legalVersions.privacy_version,
        },
        idempotencyKeyRef.current,
      );
    },
    onSuccess: (_data, values) => {
      saveRegistrationContext({
        email: values.email.trim(),
        organizationName: values.organizationName.trim(),
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
            message: errorData.message ?? errorData.title ?? GENERIC_SUBMIT_ERROR,
          });
        }
        return;
      }

      form.setError('root', { type: 'server', message: GENERIC_SUBMIT_ERROR });
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
