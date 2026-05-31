import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useMemo, useRef } from 'react';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';

import {
  createRegisterIdempotencyKey,
  fetchExternalRegistrationSession,
  registerOrganization,
  slugifyOrganizationName,
  toAdminNameParts,
} from '@/features/auth/api';
import {
  type RegisterCompleteFormValues,
  registerCompleteSchema,
} from '@/features/auth/schemas/register-complete-schema';
import type { RegisterValidationErrorData } from '@/features/auth/types';
import { LEGAL_VERSION } from '@/features/auth/types';
import { ApiError } from '@/lib/api';

const DEFAULT_SUCCESS_MESSAGE =
  'Registration successful. Please check your email to verify your account.';

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
  form: UseFormReturn<RegisterCompleteFormValues>,
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
  const termsError = pickFirstError(
    errorData.errors,
    'AcceptedTermsVersion',
    'accepted_terms_version',
    'AcceptedPrivacyVersion',
    'accepted_privacy_version',
  );

  const setFieldError = (field: FieldPath<RegisterCompleteFormValues>, message: string) => {
    form.setError(field, { type: 'server', message });
    hasMappedFieldError = true;
  };

  if (organizationNameError) {
    setFieldError('organizationName', organizationNameError);
  }
  if (fullNameError) {
    setFieldError('fullName', fullNameError);
  }
  if (termsError) {
    setFieldError('acceptedTerms', termsError);
  }

  return hasMappedFieldError;
}

export function useRegisterComplete(sessionId: string, enabled = true) {
  const idempotencyKeyRef = useRef(createRegisterIdempotencyKey());

  const sessionQuery = useQuery({
    queryKey: ['auth', 'external-registration', sessionId],
    queryFn: () => fetchExternalRegistrationSession(sessionId),
    retry: false,
    enabled: enabled && sessionId.length > 0,
  });

  const form = useForm<RegisterCompleteFormValues>({
    resolver: zodResolver(registerCompleteSchema),
    defaultValues: {
      organizationName: '',
      fullName: '',
      acceptedTerms: false,
    },
    mode: 'onSubmit',
  });

  const organizationName = form.watch('organizationName');
  const slugPreview = useMemo(() => slugifyOrganizationName(organizationName), [organizationName]);

  const mutation = useMutation({
    mutationFn: async (values: RegisterCompleteFormValues) => {
      if (!sessionQuery.data) {
        throw new Error('Registration session is unavailable.');
      }

      const names = toAdminNameParts(values.fullName);
      return registerOrganization(
        {
          org_name: values.organizationName.trim(),
          admin_first_name: names.firstName,
          admin_last_name: names.lastName,
          admin_email: sessionQuery.data.email,
          password: '',
          password_confirmation: '',
          external_registration_session_id: sessionId,
          accepted_terms_version: LEGAL_VERSION,
          accepted_privacy_version: LEGAL_VERSION,
        },
        idempotencyKeyRef.current,
      );
    },
    onSuccess: () => {
      form.reset();
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

  async function submit(values: RegisterCompleteFormValues) {
    form.clearErrors('root');
    try {
      await mutation.mutateAsync(values);
    } catch {
      // Field and submit errors are applied in mutation.onError.
    }
  }

  function resetFlow() {
    idempotencyKeyRef.current = createRegisterIdempotencyKey();
    mutation.reset();
    form.clearErrors();
    form.reset();
  }

  const successMessage = mutation.isSuccess
    ? (mutation.data?.message ?? DEFAULT_SUCCESS_MESSAGE)
    : null;

  return {
    form,
    sessionQuery,
    slugPreview,
    loading: mutation.isPending,
    successMessage,
    submit,
    resetFlow,
  };
}
