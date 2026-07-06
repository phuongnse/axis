import { zodResolver } from '@hookform/resolvers/zod';
import { useMutation } from '@tanstack/react-query';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useMemo, useRef, useState } from 'react';
import { type FieldPath, type UseFormReturn, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { createRegisterIdempotencyKey, registerUser } from '@/features/auth/api';
import { useLegalVersions } from '@/features/auth/hooks/useLegalVersions';
import { useRefreshClientValidationErrors } from '@/features/auth/hooks/useRefreshClientValidationErrors';
import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '@/features/auth/password-policy';
import {
  getFirstFieldError,
  getRegisterFieldError,
  getRegisterProblemFieldError,
  type LocalizedErrorDescriptor,
} from '@/features/auth/problem-details';
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

type RegisterRootErrorKind = 'generic' | 'legal_versions_missing';
type LocalizedRegisterFieldErrors = Partial<
  Record<FieldPath<RegisterFormValues>, LocalizedErrorDescriptor>
>;

function hasLegalVersions(
  legalVersions: LegalVersionsResponse | undefined,
): legalVersions is LoadedLegalVersions {
  return Boolean(legalVersions?.termsVersion && legalVersions.privacyVersion);
}

function applyRegisterValidationErrors(
  form: UseFormReturn<RegisterFormValues>,
  errorData: RegisterValidationErrorData,
  t: ReturnType<typeof useTranslation>['t'],
): { hasMappedFieldError: boolean; localizedFieldErrors: LocalizedRegisterFieldErrors } {
  let hasMappedFieldError = false;
  const localizedFieldErrors: LocalizedRegisterFieldErrors = {};

  const fullNameError = getFirstFieldError(errorData, 'fullName');
  const emailError = getFirstFieldError(errorData, 'email');
  const passwordError = getFirstFieldError(errorData, 'password');
  const passwordConfirmationError = getFirstFieldError(errorData, 'passwordConfirmation');
  const termsError =
    getFirstFieldError(errorData, 'acceptedTermsVersion') ??
    getFirstFieldError(errorData, 'acceptedPrivacyVersion');

  const setFieldError = (field: FieldPath<RegisterFormValues>, message: string) => {
    const localizedError = getRegisterFieldError(errorData, field);
    if (localizedError) {
      localizedFieldErrors[field] = localizedError;
      form.setError(field, {
        type: 'server',
        message: getLocalizedErrorMessage(localizedError, t),
      });
    } else {
      form.setError(field, { type: 'server', message });
    }
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

  return { hasMappedFieldError, localizedFieldErrors };
}

function getLocalizedErrorMessage(
  error: LocalizedErrorDescriptor,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  return t(error.key, error.values);
}

function getRegisterRootErrorMessage(
  errorKind: RegisterRootErrorKind,
  t: ReturnType<typeof useTranslation>['t'],
): string {
  switch (errorKind) {
    case 'generic':
      return t('auth.genericError');
    case 'legal_versions_missing':
      return t('auth.legalDocumentsMissing');
  }
}

export function useRegister() {
  const navigate = useNavigate();
  const { t } = useTranslation();
  const language = currentSiteLanguage();
  const [submitErrorKind, setSubmitErrorKind] = useState<RegisterRootErrorKind | null>(null);
  const [localizedFieldErrors, setLocalizedFieldErrors] = useState<LocalizedRegisterFieldErrors>(
    {},
  );
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

  useEffect(() => {
    for (const [field, error] of Object.entries(localizedFieldErrors)) {
      if (!error) continue;
      form.setError(field as FieldPath<RegisterFormValues>, {
        type: 'server',
        message: getLocalizedErrorMessage(error, t),
      });
    }
  }, [form, localizedFieldErrors, t]);

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
          preferredLanguage: currentSiteLanguage(),
        },
        idempotencyKeyRef.current,
      );
    },
    onSuccess: (_data, { values }) => {
      setSubmitErrorKind(null);
      setLocalizedFieldErrors({});
      saveRegistrationContext({
        email: values.email.trim(),
      });
      form.reset();
      void navigate({ to: '/register/confirmation' });
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError && error.status === 409) {
        const fieldError = getRegisterProblemFieldError(error);
        if (fieldError) {
          setLocalizedFieldErrors({ [fieldError.field]: fieldError.error });
          form.setError(fieldError.field, {
            type: 'server',
            message: getLocalizedErrorMessage(fieldError.error, t),
          });
          return;
        }

        setSubmitErrorKind('generic');
        return;
      }

      if (error instanceof ApiError && error.status < 500) {
        const errorData = error.data as RegisterValidationErrorData;
        const { hasMappedFieldError, localizedFieldErrors } = applyRegisterValidationErrors(
          form,
          errorData,
          t,
        );
        setLocalizedFieldErrors(localizedFieldErrors);
        if (!hasMappedFieldError) {
          setSubmitErrorKind('generic');
        }
        return;
      }

      setSubmitErrorKind('generic');
    },
  });

  async function submit(values: RegisterFormValues) {
    setSubmitErrorKind(null);
    setLocalizedFieldErrors({});
    form.clearErrors();
    if (legalVersionsLoading || legalVersionsError || !hasLegalVersions(legalVersions)) {
      setSubmitErrorKind('legal_versions_missing');
      return;
    }

    await mutation.mutateAsync({ values, legalVersions }).catch(() => undefined);
  }

  return {
    form,
    loading: mutation.isPending || legalVersionsLoading,
    submit,
    submitError: submitErrorKind ? getRegisterRootErrorMessage(submitErrorKind, t) : null,
    legalVersions,
  };
}
