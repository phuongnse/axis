import { fetchApi } from '@/lib/api';
import { useAuthStore } from './auth-store';
import { CLIENT_ID, clearPkceSession, loadPkceSession, REDIRECT_URI } from './pkce';
import type { RegisterOrganizationRequest, RegisterOrganizationResponse } from './types';

interface TokenResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
}

export function createRegisterIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `register-${Date.now()}`;
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

/** Best-effort server sign-out; callers must clear local session regardless of outcome (US-015). */
export async function signOut(): Promise<void> {
  try {
    await fetch('/api/auth/signout', { method: 'POST', credentials: 'include' });
  } catch {
    // Network failure — local cleanup is handled by the caller.
  }
}
