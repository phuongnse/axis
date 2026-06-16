import type { components } from '@/lib/api-types';

// Derived from the backend OpenAPI schema so request/response
// shapes can never drift from the API. Regenerate with `npm run gen:api-types`.
export type RegisterTeamAccountRequest = components['schemas']['RegisterTeamAccountRequest'];
export type RegisterUserRequest = components['schemas']['RegisterUserRequest'];
export type MessageResponse = components['schemas']['MessageResponse'];

export type LegalVersionsResponse = components['schemas']['LegalVersionsDto'];

export type TeamAccountSlugPreviewResponse = components['schemas']['TeamAccountSlugPreviewDto'];

export interface RegisterValidationErrorData {
  errors?: Record<string, string[]>;
  message?: string;
  title?: string;
}

export interface LoginCredentials {
  email: string;
  password: string;
}

export interface LoginAttemptResult {
  authorizeUrl: string;
  location: string | null;
}

export type VerifyEmailResponse = components['schemas']['VerifyEmailSessionEstablishedDto'];
export type ProvisioningStatusResponse = components['schemas']['ProvisioningStatusDto'];
export type ModuleProvisioningStatus = components['schemas']['ModuleProvisioningStatusDto'];

export type VerifyEmailErrorKind = 'expired' | 'already_used' | 'invalid' | 'rate_limited';

export type ResendVerificationState = 'idle' | 'sending' | 'success' | 'rate_limited' | 'error';
