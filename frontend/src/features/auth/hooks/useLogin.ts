import { zodResolver } from '@hookform/resolvers/zod';
import { useState } from 'react';
import { useForm } from 'react-hook-form';

import { buildAuthorizeUrl, createPkceSession } from '@/features/auth/pkce';
import { type LoginFormValues, loginSchema } from '@/features/auth/schemas/login-schema';

export type LoginErrorKind = 'credentials' | 'unverified' | 'deactivated' | 'locked' | 'server';

export interface LoginError {
  kind: LoginErrorKind;
  message: string;
}

function mapLoginFailure(status: number, bodyText: string): LoginError {
  const lower = bodyText.toLowerCase();
  if (lower.includes('verify') || lower.includes('unverified'))
    return {
      kind: 'unverified',
      message: 'Please verify your email before signing in.',
    };
  if (lower.includes('deactivated'))
    return {
      kind: 'deactivated',
      message: 'Your account has been deactivated. Contact your organization admin.',
    };
  if (lower.includes('too many') || lower.includes('locked'))
    return {
      kind: 'locked',
      message: 'Too many failed attempts. Try again later.',
    };
  if (status === 401) return { kind: 'credentials', message: 'Incorrect email or password' };
  return { kind: 'server', message: 'Something went wrong. Please try again.' };
}

export function useLogin() {
  const [loginError, setLoginError] = useState<LoginError | null>(null);
  const [loading, setLoading] = useState(false);

  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: { email: '', password: '' },
    mode: 'onSubmit',
  });

  async function submit(values: LoginFormValues) {
    setLoginError(null);
    setLoading(true);

    try {
      const pkce = createPkceSession();
      const authorizeUrl = await buildAuthorizeUrl(pkce.state, pkce.verifier);
      const body = new URLSearchParams({
        email: values.email.trim(),
        password: values.password,
        return_url: `${window.location.origin}${authorizeUrl}`,
      });

      const response = await fetch('/connect/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body,
        credentials: 'include',
        redirect: 'manual',
      });

      const isRedirect =
        response.status === 302 ||
        response.status === 303 ||
        response.status === 307 ||
        response.status === 308 ||
        response.type === 'opaqueredirect';

      if (!isRedirect && !response.ok) {
        const bodyText = await response.text();
        const mapped = mapLoginFailure(response.status, bodyText);
        setLoginError(mapped);
        if (mapped.kind === 'server') {
          form.setValue('password', '');
        }
        setLoading(false);
        return;
      }

      const location = response.headers.get('Location');
      if (location) {
        window.location.href = location.startsWith('http')
          ? location
          : `${window.location.origin}${location}`;
        return;
      }

      window.location.href = authorizeUrl;
    } catch {
      setLoginError({ kind: 'server', message: 'Something went wrong. Please try again.' });
      form.setValue('password', '');
      setLoading(false);
    }
  }

  return { form, loginError, loading, submit };
}
