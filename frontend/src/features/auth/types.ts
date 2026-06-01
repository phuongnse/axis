import type { components } from '@/lib/api-types';

// Derived from the backend OpenAPI schema (snake_case) so request/response
// shapes can never drift from the API. Regenerate with `npm run gen:api-types`.
export type RegisterOrganizationRequest = components['schemas']['RegisterOrganizationRequest'];

export type LegalVersionsResponse = components['schemas']['LegalVersionsDto'];

export type OrganizationSlugPreviewResponse = components['schemas']['OrganizationSlugPreviewDto'];

export interface RegisterOrganizationResponse {
  message?: string;
}

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

export interface VerifyEmailResponse {
  sessionEstablished: boolean;
}

export interface ProvisioningStatusResponse {
  organizationId: string;
  organizationStatus: string;
  isReady: boolean;
  modules: ModuleProvisioningStatus[];
}

export interface ModuleProvisioningStatus {
  module: string;
  status: string;
  attemptCount: number;
  lastError: string | null;
}

export type VerifyEmailErrorKind = 'expired' | 'already_used' | 'invalid' | 'rate_limited';

export type ResendVerificationState = 'idle' | 'sending' | 'success' | 'rate_limited' | 'error';
