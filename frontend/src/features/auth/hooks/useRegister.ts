import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useForm } from 'react-hook-form';

import {
  type RegisterFormValues,
  registerSchema,
} from '@/features/auth/schemas/register-schema';
import { ApiError, fetchApi } from '@/lib/api';

interface RegisterRequest {
  org_name: string;
  admin_first_name: string;
  admin_last_name: string;
  admin_email: string;
  password: string;
  password_confirmation: string;
}

interface RegisterResponse {
  message?: string;
}

interface ValidationErrorData {
  errors?: Record<string, string[]>;
}

function toNameParts(fullName: string): { firstName: string; lastName: string } {
  const parts = fullName
    .trim()
    .split(/\s+/)
    .filter((part) => part.length > 0);
  return {
    firstName: parts[0] ?? '',
    lastName: parts.slice(1).join(' '),
  };
}

function createIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `register-${Date.now()}`;
}

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

export function useRegister() {
  const [loading, setLoading] = useState(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const form = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      organizationName: '',
      fullName: '',
      email: '',
      password: '',
      passwordConfirmation: '',
    },
    mode: 'onSubmit',
  });

  async function submit(values: RegisterFormValues) {
    setSubmitError(null);
    setLoading(true);

    try {
      const names = toNameParts(values.fullName);
      const payload: RegisterRequest = {
        org_name: values.organizationName.trim(),
        admin_first_name: names.firstName,
        admin_last_name: names.lastName,
        admin_email: values.email.trim(),
        password: values.password,
        password_confirmation: values.passwordConfirmation,
      };

      const response = await fetchApi<RegisterResponse>('/organizations', {
        method: 'POST',
        headers: {
          'Idempotency-Key': createIdempotencyKey(),
        },
        body: JSON.stringify(payload),
      });

      setSuccessMessage(
        response?.message ??
          'Registration successful. Please check your email to verify your account.',
      );
      form.reset();
    } catch (error: unknown) {
      if (error instanceof ApiError && error.status < 500) {
        const errorData = error.data as ValidationErrorData;
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
        const emailError = pickFirstError(
          errorData.errors,
          'AdminEmail',
          'admin_email',
          'email',
        );
        const passwordError = pickFirstError(errorData.errors, 'Password', 'password');
        const passwordConfirmationError = pickFirstError(
          errorData.errors,
          'PasswordConfirmation',
          'password_confirmation',
        );

        if (organizationNameError) {
          form.setError('organizationName', { type: 'server', message: organizationNameError });
        }
        if (fullNameError) {
          form.setError('fullName', { type: 'server', message: fullNameError });
        }
        if (emailError) {
          form.setError('email', { type: 'server', message: emailError });
        }
        if (passwordError) {
          form.setError('password', { type: 'server', message: passwordError });
        }
        if (passwordConfirmationError) {
          form.setError('passwordConfirmation', {
            type: 'server',
            message: passwordConfirmationError,
          });
        }
      } else {
        setSubmitError('Something went wrong, please try again');
      }
    } finally {
      setLoading(false);
    }
  }

  function resetFlow() {
    setSuccessMessage(null);
    setSubmitError(null);
  }

  return {
    form,
    loading,
    submitError,
    successMessage,
    submit,
    resetFlow,
  };
}
