import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useMemo, useRef } from 'react';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { createRegisterIdempotencyKey, registerUser } from '@/features/auth/api';
import { useLegalVersions } from '@/features/auth/hooks/useLegalVersions';
import { useRefreshClientValidationErrors } from '@/features/auth/hooks/useRefreshClientValidationErrors';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '@/features/auth/password-policy';
import { getProblemDetail } from '@/features/auth/problem-details';
import { saveRegistrationContext } from '@/features/auth/registration-context';
import {
  createRegisterSchema,
  type RegisterFormValues,
} from '@/features/auth/schemas/register-schema';
import type { LegalVersionsResponse, RegisterValidationErrorData } from '@/features/auth/types';
import { currentSiteLanguage } from '@/features/preferences';
import { ApiError } from '@/lib/api';

type LoadedLegalVersions = LegalVersionsResponse & {
  termsVersion: string;
  privacyVersion: string;
};

const clientValidationFields: FieldPath<RegisterFormValues>[] = [
  'fullName',
  'email',
  'password',
  'passwordConfirmation',
  'acceptedTerms',
];

interface RegisterMutationInput {
  values: RegisterFormValues;
  legalVersions: LoadedLegalVersions;
}

function hasLegalVersions(
  legalVersions: LegalVersionsResponse | undefined,
): legalVersions is LoadedLegalVersions {
  return Boolean(legalVersions?.termsVersion && legalVersions.privacyVersion);
}

function getFieldError(
  errors: Record<string, string[]> | undefined,
  key: string,
): string | undefined {
  return errors?.[key]?.[0];
}

function applyRegisterValidationErrors(
  form: UseFormReturn<RegisterFormValues>,
  errorData: RegisterValidationErrorData,
): boolean {
  let hasMappedFieldError = false;

  const fullNameError = getFieldError(errorData.errors, 'fullName');
  const emailError = getFieldError(errorData.errors, 'email');
  const passwordError = getFieldError(errorData.errors, 'password');
  const passwordConfirmationError = getFieldError(errorData.errors, 'passwordConfirmation');
  const termsError =
    getFieldError(errorData.errors, 'acceptedTermsVersion') ??
    getFieldError(errorData.errors, 'acceptedPrivacyVersion');

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
  const navigate = useNavigate();
  const { t } = useTranslation();
  const language = currentSiteLanguage();
  const {
    data: legalVersions,
    isError: legalVersionsError,
    isLoading: legalVersionsLoading,
  } = useLegalVersions();
  const idempotencyKeyRef = useRef(createRegisterIdempotencyKey());
  const registerSchema = useMemo(
    () =>
      createRegisterSchema({
        fullNameRequired: t('validation.fullNameRequired'),
        emailRequired: t('validation.emailRequired'),
        emailInvalid: t('validation.emailInvalid'),
        passwordRequired: t('validation.passwordRequired'),
        passwordMin: t('validation.passwordMin', { count: PASSWORD_MIN_LENGTH }),
        passwordMax: t('validation.passwordMax', { count: PASSWORD_MAX_LENGTH }),
        passwordConfirmationRequired: t('validation.passwordConfirmationRequired'),
        acceptTerms: t('validation.acceptTerms'),
        passwordMismatch: t('validation.passwordMismatch'),
        passwordHarder: t('validation.passwordHarder'),
      }),
    [t],
  );
  const genericSubmitError = t('auth.genericError');
  const legalVersionsMissingError = t('auth.legalDocumentsMissing');
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
  useRefreshClientValidationErrors(form, clientValidationFields, language);

  const mutation = useMutation({
    mutationFn: async ({ values, legalVersions }: RegisterMutationInput) => {
      return registerUser(
        {
          fullName: values.fullName.trim(),
          email: values.email.trim(),
          password: values.password,
          passwordConfirmation: values.passwordConfirmation,
          acceptedTermsVersion: legalVersions.termsVersion,
          acceptedPrivacyVersion: legalVersions.privacyVersion,
        },
        idempotencyKeyRef.current,
      );
    },
    onSuccess: (_data, { values }) => {
      saveRegistrationContext({
        email: values.email.trim(),
      });
      form.reset();
      void navigate({ to: '/register/confirmation' });
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError && error.status === 409) {
        form.setError('email', {
          type: 'server',
          message: getProblemDetail(error),
        });
        return;
      }

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
    if (legalVersionsLoading || legalVersionsError || !hasLegalVersions(legalVersions)) {
      form.setError('root', {
        type: 'server',
        message: legalVersionsMissingError,
      });
      return;
    }

    await mutation.mutateAsync({ values, legalVersions }).catch(() => undefined);
  }

  return {
    form,
    loading: mutation.isPending || legalVersionsLoading,
    submit,
    legalVersions,
  };
}
