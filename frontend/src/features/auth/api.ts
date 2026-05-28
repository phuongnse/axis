import { fetchApi } from '@/lib/api';
import { useAuthStore } from './auth-store';
import {
  buildAuthorizeUrl,
  CLIENT_ID,
  clearPkceSession,
  createPkceSession,
  loadPkceSession,
  REDIRECT_URI,
} from './pkce';
import type {
  LoginAttemptResult,
  LoginCredentials,
  RegisterOrganizationRequest,
  RegisterOrganizationResponse,
} from './types';

interface TokenResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
}

export function createRegisterIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `register-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

export function toAdminNameParts(fullName: string): { firstName: string; lastName: string } {
  const parts = fullName
    .trim()
    .split(/\s+/)
    .filter((part) => part.length > 0);
  return {
    firstName: parts[0] ?? '',
    lastName: parts.slice(1).join(' '),
  };
}

export async function registerOrganization(
  payload: RegisterOrganizationRequest,
  idempotencyKey: string,
): Promise<RegisterOrganizationResponse> {
  return fetchApi<RegisterOrganizationResponse>('/organizations', {
    method: 'POST',
    headers: {
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify(payload),
  });
}

export class LoginRequestError extends Error {
  status: number;
  bodyText: string;

  constructor(status: number, bodyText: string) {
    super('Login request failed');
    this.status = status;
    this.bodyText = bodyText;
    this.name = 'LoginRequestError';
  }
}

export async function loginWithPassword(
  credentials: LoginCredentials,
): Promise<LoginAttemptResult> {
  const pkce = createPkceSession();
  const authorizeUrl = await buildAuthorizeUrl(pkce.state, pkce.verifier);
  const body = new URLSearchParams({
    email: credentials.email.trim(),
    password: credentials.password,
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
    throw new LoginRequestError(response.status, bodyText);
  }

  return {
    authorizeUrl,
    location: response.headers.get('Location'),
  };
}

export async function exchangeAuthorizationCode(code: string): Promise<string> {
  const pkce = loadPkceSession();
  if (!pkce) {
    throw new Error('Missing PKCE session. Please sign in again.');
  }

  const body = new URLSearchParams({
    grant_type: 'authorization_code',
    code,
    redirect_uri: REDIRECT_URI,
    client_id: CLIENT_ID,
    code_verifier: pkce.verifier,
  });

  const response = await fetch('/connect/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body,
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error('Token exchange failed');
  }

  const data = (await response.json()) as TokenResponse;
  clearPkceSession();
  useAuthStore.getState().setSession(data.access_token);
  return data.access_token;
}

/** Best-effort server sign-out; callers must clear local session regardless of outcome. */
export async function signOut(): Promise<void> {
  try {
    await fetch('/api/auth/signout', { method: 'POST', credentials: 'include' });
  } catch {
    // Network failure — local cleanup is handled by the caller.
  }
}
