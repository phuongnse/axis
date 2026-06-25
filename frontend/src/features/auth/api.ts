import { fetchApi } from '@/lib/api';
import { useAuthStore } from './auth-store';
import {
  buildAuthorizeUrl,
  CLIENT_ID,
  clearPkceSession,
  connectEndpoint,
  createPkceSession,
  loadPkceSession,
  REDIRECT_URI,
} from './pkce';
import type {
  LegalVersionsResponse,
  MessageResponse,
  RegisterUserRequest,
  VerifyEmailResponse,
} from './types';

export const authKeys = {
  all: ['auth'] as const,
  legalVersions: ['auth', 'legal-versions'] as const,
};

export function createRegisterIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `register-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

const verifyEmailSuccessCache = new Map<
  string,
  { response: VerifyEmailResponse; expiresAt: number }
>();
const verifyEmailInFlight = new Map<string, Promise<VerifyEmailResponse>>();
const verifyEmailSuccessCacheTtlMs = 60_000;

function pruneVerifyEmailSuccessCache(now: number): void {
  for (const [token, entry] of verifyEmailSuccessCache.entries()) {
    if (entry.expiresAt <= now) {
      verifyEmailSuccessCache.delete(token);
    }
  }
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

export async function registerUser(
  payload: RegisterUserRequest,
  idempotencyKey: string,
): Promise<MessageResponse> {
  return fetchApi<MessageResponse>('/users/register', {
    method: 'POST',
    headers: {
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify(payload),
  });
}

export async function getLegalVersions(): Promise<LegalVersionsResponse> {
  return fetchApi<LegalVersionsResponse>('/legal/versions');
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

  const response = await fetch(connectEndpoint('/connect/token'), {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body,
    credentials: 'include',
  });

  if (!response.ok) {
    throw new Error('Token exchange failed');
  }

  const data = await response.json();
  const accessToken = readAccessToken(data);
  clearPkceSession();
  useAuthStore.getState().setSession(accessToken);
  return accessToken;
}

export async function verifyEmail(token: string): Promise<VerifyEmailResponse> {
  const now = Date.now();
  pruneVerifyEmailSuccessCache(now);

  const cached = verifyEmailSuccessCache.get(token);
  if (cached) {
    return cached.response;
  }

  const inFlight = verifyEmailInFlight.get(token);
  if (inFlight) {
    return inFlight;
  }

  const request = fetchApi<VerifyEmailResponse>('/auth/verify-email', {
    method: 'POST',
    body: JSON.stringify({ token }),
  })
    .then((response) => {
      verifyEmailSuccessCache.set(token, {
        response,
        expiresAt: Date.now() + verifyEmailSuccessCacheTtlMs,
      });
      return response;
    })
    .finally(() => {
      verifyEmailInFlight.delete(token);
    });

  verifyEmailInFlight.set(token, request);
  return request;
}

export async function completePostVerifyPkceFlow(): Promise<void> {
  const pkce = createPkceSession();
  const authorizeUrl = await buildAuthorizeUrl(pkce.state, pkce.verifier);
  window.location.assign(authorizeUrl);
}

function readAccessToken(data: unknown): string {
  if (!data || typeof data !== 'object') {
    throw new Error('Token exchange returned an invalid response');
  }

  const accessToken = (data as Record<string, unknown>).access_token;
  if (typeof accessToken !== 'string' || accessToken.length === 0) {
    throw new Error('Token exchange returned an invalid response');
  }

  return accessToken;
}

export async function resendVerificationEmail(email: string): Promise<void> {
  await fetchApi<null>('/auth/resend-verification', {
    method: 'POST',
    body: JSON.stringify({ email }),
  });
}
