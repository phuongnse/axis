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
import { storePostVerifyProvisioningToken } from './post-verify-session';
import type {
  LegalVersionsResponse,
  LoginAttemptResult,
  LoginCredentials,
  MessageResponse,
  ProvisioningStatusResponse,
  RegisterUserRequest,
  RegisterWorkspaceRequest,
  VerifyEmailResponse,
  WorkspaceSlugPreviewResponse,
} from './types';

export const authKeys = {
  all: ['auth'] as const,
  provisioningStatus: (token: string) => [...authKeys.all, 'provisioning-status', token] as const,
  legalVersions: ['auth', 'legal-versions'] as const,
  slugPreview: (workspaceName: string) => [...authKeys.all, 'slug-preview', workspaceName] as const,
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

export async function registerWorkspace(
  payload: RegisterWorkspaceRequest,
  idempotencyKey: string,
): Promise<MessageResponse> {
  return fetchApi<MessageResponse>('/workspaces', {
    method: 'POST',
    headers: {
      'Idempotency-Key': idempotencyKey,
    },
    body: JSON.stringify(payload),
  });
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

export async function getWorkspaceSlugPreview(
  workspaceName: string,
): Promise<WorkspaceSlugPreviewResponse> {
  const params = new URLSearchParams({ workspaceName });
  return fetchApi<WorkspaceSlugPreviewResponse>(`/workspaces/slug-preview?${params.toString()}`);
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
    return_url: authorizeUrl,
  });

  const response = await fetch(connectEndpoint('/connect/login'), {
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

/**
 * After verify-email establishes a session cookie, run PKCE so the SPA receives tokens.
 * Stores the verification token for the callback to redirect to provisioning.
 */
export async function completePostVerifyPkceFlow(verificationToken?: string | null): Promise<void> {
  if (verificationToken) {
    storePostVerifyProvisioningToken(verificationToken);
  }
  const pkce = createPkceSession();
  const authorizeUrl = await buildAuthorizeUrl(pkce.state, pkce.verifier);
  window.location.assign(authorizeUrl);
}

export async function switchWorkspace(workspaceId: string): Promise<void> {
  const pkce = createPkceSession();
  const authorizeUrl = await buildAuthorizeUrl(pkce.state, pkce.verifier);
  await fetchApi<null>('/auth/switch-workspace', {
    method: 'POST',
    body: JSON.stringify({ workspaceId }),
  });
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

export async function getProvisioningStatus(token: string): Promise<ProvisioningStatusResponse> {
  const params = new URLSearchParams({ token });
  return fetchApi<ProvisioningStatusResponse>(`/auth/provisioning-status?${params.toString()}`);
}

export async function retryProvisioning(token: string): Promise<void> {
  await fetchApi<null>('/auth/retry-provisioning', {
    method: 'POST',
    body: JSON.stringify({ token }),
  });
}

/** Best-effort server sign-out; callers must clear local session regardless of outcome. */
export async function signOut(): Promise<void> {
  try {
    await fetch('/api/auth/signout', { method: 'POST', credentials: 'include' });
  } catch {
    // Network failure - local cleanup is handled by the caller.
  }
}
