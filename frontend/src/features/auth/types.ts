export interface RegisterOrganizationRequest {
  org_name: string;
  admin_first_name: string;
  admin_last_name: string;
  admin_email: string;
  password: string;
  password_confirmation: string;
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

export type ResendVerificationState = 'idle' | 'sending' | 'success' | 'rate_limited';
