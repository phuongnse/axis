export interface RegisterOrganizationRequest {
  org_name: string;
  admin_first_name: string;
  admin_last_name: string;
  admin_email: string;
  password: string;
  password_confirmation: string;
  accepted_terms_version: string;
  accepted_privacy_version: string;
}

export interface LegalVersionsResponse {
  termsVersion: string;
  privacyVersion: string;
}

export interface OrganizationSlugPreviewResponse {
  slug: string;
}

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
