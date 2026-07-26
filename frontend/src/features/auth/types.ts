import type * as ApiTypes from '@/lib/api-generated';

// Derived from the backend OpenAPI schema so request/response
// shapes can never drift from the API. Regenerate with `npm run gen:api-types`.
export type RegisterUserRequest = ApiTypes.RegisterUserRequest;
export type MessageResponse = ApiTypes.MessageResponse;
export type SignInUserRequest = ApiTypes.SignInUserRequest;
export type SignInResponse = ApiTypes.SignInSessionEstablishedDto;

export type LegalVersionsResponse = ApiTypes.LegalVersionsDto;

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

export type VerifyEmailResponse = ApiTypes.VerifyEmailSessionEstablishedDto;

export type VerifyEmailErrorKind = 'expired' | 'already_used' | 'invalid' | 'rate_limited';

export type ResendVerificationState = 'idle' | 'sending' | 'success' | 'rate_limited' | 'error';
