export interface RegisterOrganizationRequest {
  org_name: string;
  admin_first_name: string;
  admin_last_name: string;
  admin_email: string;
  password: string;
  password_confirmation: string;
  external_registration_session_id?: string;
  accepted_terms_version?: string;
  accepted_privacy_version?: string;
}

export interface RegisterOrganizationResponse {
  message?: string;
}

export interface ExternalProvidersResponse {
  providers: string[];
}

export interface ExternalRegistrationSessionResponse {
  email: string;
  display_name: string;
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

export type ExternalProviderId = 'microsoft' | 'google' | 'github';

// Legal document version sent when completing external (SSO) registration.
// NOTE: the legal slice (PR #154) introduces GET /api/legal/versions and a
// server-side WellKnownLegalDocuments constant that the email/password form
// already consumes. When this slice is rebased onto that one, this constant
// must be replaced by the value from useLegalVersions() so the completion form
// and the RegisterOrganization validator agree on the accepted version.
export const LEGAL_VERSION = '1.0';

export const EXTERNAL_PROVIDER_LABELS: Record<ExternalProviderId, string> = {
  microsoft: 'Microsoft',
  google: 'Google',
  github: 'GitHub',
};
