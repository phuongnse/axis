import { ApiError, fetchApi } from '@/lib/api';
import type { AxisBrowserSessionDto } from '@/lib/api-generated';
import { applyBrowserSessionResponse, getBrowserSessionStatus, useAuthStore } from './auth-store';
import {
  type AuthorizationRequestContinuation,
  buildAuthorizationRequestResumeUrl,
} from './authorization-request';
import type {
  LegalVersionsResponse,
  MessageResponse,
  RegisterUserRequest,
  SignInResponse,
  SignInUserRequest,
  VerifyEmailResponse,
} from './types';

export class BrowserSessionUnavailableError extends Error {
  constructor() {
    super('The browser session could not be resolved.');
    this.name = 'BrowserSessionUnavailableError';
  }
}

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
let browserSessionRestoreInFlight: Promise<boolean> | null = null;

interface BrowserSessionRestoreOptions {
  force?: boolean;
}

function pruneVerifyEmailSuccessCache(now: number): void {
  for (const [token, entry] of verifyEmailSuccessCache.entries()) {
    if (entry.expiresAt <= now) verifyEmailSuccessCache.delete(token);
  }
}

export async function registerUser(
  payload: RegisterUserRequest,
  idempotencyKey: string,
): Promise<MessageResponse> {
  return fetchApi<MessageResponse>('/users/register', {
    method: 'POST',
    headers: { 'Idempotency-Key': idempotencyKey },
    body: JSON.stringify(payload),
  });
}

export async function signInUser(payload: SignInUserRequest): Promise<SignInResponse> {
  const response = await fetchApi<SignInResponse>('/auth/sign-in', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
  if (response.sessionEstablished && !(await restoreBrowserSession({ force: true }))) {
    throw new Error('The browser session was not established after sign-in.');
  }
  return response;
}

export async function signOutUser(): Promise<void> {
  await fetchApi<null>('/auth/sign-out', { method: 'POST' });
  useAuthStore.getState().markBrowserSessionGuest();
}

export async function getLegalVersions(): Promise<LegalVersionsResponse> {
  return fetchApi<LegalVersionsResponse>('/legal/versions');
}

export async function verifyEmail(token: string): Promise<VerifyEmailResponse> {
  const now = Date.now();
  pruneVerifyEmailSuccessCache(now);

  const cached = verifyEmailSuccessCache.get(token);
  if (cached) return cached.response;

  const inFlight = verifyEmailInFlight.get(token);
  if (inFlight) return inFlight;

  const request = fetchApi<VerifyEmailResponse>('/auth/verify-email', {
    method: 'POST',
    body: JSON.stringify({ token }),
  })
    .then(async (response) => {
      if (response.sessionEstablished && !(await restoreBrowserSession({ force: true }))) {
        throw new Error('The browser session was not established after email verification.');
      }
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

export async function restoreBrowserSession(
  options: BrowserSessionRestoreOptions = {},
): Promise<boolean> {
  const status = getBrowserSessionStatus();
  if (!options.force && status !== 'unknown') return status === 'authenticated';

  if (!browserSessionRestoreInFlight) {
    browserSessionRestoreInFlight = fetchApi<AxisBrowserSessionDto>('/auth/session')
      .then(applyBrowserSessionResponse)
      .catch((error: unknown) => {
        useAuthStore.getState().clearSession();
        if (error instanceof ApiError && error.status === 401) return false;
        throw new BrowserSessionUnavailableError();
      })
      .finally(() => {
        browserSessionRestoreInFlight = null;
      });
  }
  return browserSessionRestoreInFlight;
}

export async function completePostVerifyFlow(): Promise<boolean> {
  return restoreBrowserSession({ force: true });
}

export async function hasPendingWorkspaceInvitation(): Promise<boolean> {
  try {
    const state = await fetchApi<{ active: boolean }>('/internal/workspace-invitations/handoff');
    return state.active;
  } catch {
    return false;
  }
}

export async function completePostSignInFlow(
  authorizationRequest?: AuthorizationRequestContinuation,
): Promise<boolean> {
  if (authorizationRequest) {
    window.location.assign(buildAuthorizationRequestResumeUrl(authorizationRequest));
    return false;
  }
  return getBrowserSessionStatus() === 'authenticated';
}

export async function resendVerificationEmail(email: string): Promise<void> {
  await fetchApi<null>('/auth/resend-verification', {
    method: 'POST',
    body: JSON.stringify({ email }),
  });
}
