import type { components } from '@/lib/api-types';

// Derived from the backend OpenAPI schema so request/response
// shapes can never drift from the API. Regenerate with `npm run gen:api-types`.
export type RegisterUserRequest = components['schemas']['RegisterUserRequest'];
export type MessageResponse = components['schemas']['MessageResponse'];
export type SignInUserRequest = components['schemas']['SignInUserRequest'];
export type SignInResponse = components['schemas']['SignInSessionEstablishedDto'];

export type LegalVersionsResponse = components['schemas']['LegalVersionsDto'];

export interface RegisterValidationErrorData {
  errors?: Record<string, string[]>;
  errorCodes?: Record<string, string[]>;
  message?: string;
  title?: string;
}

export interface SignInValidationErrorData {
  errors?: Record<string, string[]>;
  errorCodes?: Record<string, string[]>;
  message?: string;
  title?: string;
}

export type VerifyEmailResponse = components['schemas']['VerifyEmailSessionEstablishedDto'];

export type VerifyEmailErrorKind = 'expired' | 'already_used' | 'invalid' | 'rate_limited';

export type ResendVerificationState = 'idle' | 'sending' | 'success' | 'rate_limited' | 'error';
