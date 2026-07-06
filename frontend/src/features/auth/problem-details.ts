import type { TranslationKey } from '@/features/preferences/translations';
import type { ApiError } from '@/lib/api';

export const identityProblemCodes = {
  registerFullNameRequired: 'identity.register.fullNameRequired',
  registerEmailRequired: 'identity.register.emailRequired',
  registerEmailInvalid: 'identity.register.emailInvalid',
  registerEmailAlreadyExists: 'identity.register.emailAlreadyExists',
  registerTermsCurrentRequired: 'identity.register.termsCurrentRequired',
  registerPrivacyCurrentRequired: 'identity.register.privacyCurrentRequired',
  registerPasswordRequired: 'identity.register.passwordRequired',
  registerPasswordPolicyFailed: 'identity.register.passwordPolicyFailed',
  registerPasswordConfirmationMismatch: 'identity.register.passwordConfirmationMismatch',
  signInEmailRequired: 'identity.signIn.emailRequired',
  signInEmailInvalid: 'identity.signIn.emailInvalid',
  signInPasswordRequired: 'identity.signIn.passwordRequired',
  signInInvalidCredentials: 'identity.signIn.invalidCredentials',
  signInVerificationRequired: 'identity.signIn.verificationRequired',
  signInAccountUnavailable: 'identity.signIn.accountUnavailable',
  emailVerificationInvalidToken: 'identity.emailVerification.invalidToken',
  emailVerificationExpiredToken: 'identity.emailVerification.expiredToken',
  emailVerificationAlreadyUsedToken: 'identity.emailVerification.alreadyUsedToken',
  emailVerificationAccountUnavailable: 'identity.emailVerification.accountUnavailable',
  emailVerificationResendRateLimited: 'identity.emailVerification.resendRateLimited',
} as const;

type IdentityProblemCode = (typeof identityProblemCodes)[keyof typeof identityProblemCodes];

export type LocalizedErrorDescriptor = {
  key: TranslationKey;
  values?: Record<string, string | number>;
};

export type SignInErrorKind =
  | 'verification_required'
  | 'rate_limited'
  | 'workspace_unavailable'
  | 'form';
export type SignInSubmitErrorKind = SignInErrorKind | 'generic';

type SignInProblemCode =
  | typeof identityProblemCodes.signInInvalidCredentials
  | typeof identityProblemCodes.signInVerificationRequired
  | typeof identityProblemCodes.signInAccountUnavailable;

const signInProblemKinds = {
  [identityProblemCodes.signInInvalidCredentials]: 'form',
  [identityProblemCodes.signInVerificationRequired]: 'verification_required',
  [identityProblemCodes.signInAccountUnavailable]: 'workspace_unavailable',
} satisfies Record<SignInProblemCode, SignInErrorKind>;

const signInCopyKeys = {
  form: 'auth.signInCredentialError',
  verification_required: 'auth.signInVerificationRequired',
  workspace_unavailable: 'auth.signInWorkspaceUnavailable',
  rate_limited: 'auth.signInRateLimited',
  generic: 'auth.genericError',
} satisfies Record<SignInSubmitErrorKind, TranslationKey>;

const signInFieldCopyKeys: Partial<Record<string, TranslationKey>> = {
  [identityProblemCodes.signInEmailRequired]: 'validation.emailRequired',
  [identityProblemCodes.signInEmailInvalid]: 'validation.emailInvalid',
  [identityProblemCodes.signInPasswordRequired]: 'validation.passwordRequired',
} satisfies Partial<Record<IdentityProblemCode, TranslationKey>>;

const registerFieldCopyKeys: Partial<Record<string, TranslationKey>> = {
  [identityProblemCodes.registerFullNameRequired]: 'validation.fullNameRequired',
  [identityProblemCodes.registerEmailRequired]: 'validation.emailRequired',
  [identityProblemCodes.registerEmailInvalid]: 'validation.emailInvalid',
  [identityProblemCodes.registerEmailAlreadyExists]: 'auth.registerDuplicateEmail',
  [identityProblemCodes.registerTermsCurrentRequired]: 'validation.acceptTerms',
  [identityProblemCodes.registerPrivacyCurrentRequired]: 'validation.acceptTerms',
  [identityProblemCodes.registerPasswordRequired]: 'validation.passwordRequired',
  [identityProblemCodes.registerPasswordPolicyFailed]: 'validation.passwordHarder',
  [identityProblemCodes.registerPasswordConfirmationMismatch]: 'validation.passwordMismatch',
} satisfies Partial<Record<IdentityProblemCode, TranslationKey>>;

type VerifyEmailProblemCode =
  | typeof identityProblemCodes.emailVerificationInvalidToken
  | typeof identityProblemCodes.emailVerificationExpiredToken
  | typeof identityProblemCodes.emailVerificationAlreadyUsedToken
  | typeof identityProblemCodes.emailVerificationAccountUnavailable
  | typeof identityProblemCodes.emailVerificationResendRateLimited;

const verifyEmailProblemKinds = {
  [identityProblemCodes.emailVerificationInvalidToken]: 'invalid',
  [identityProblemCodes.emailVerificationExpiredToken]: 'expired',
  [identityProblemCodes.emailVerificationAlreadyUsedToken]: 'already_used',
  [identityProblemCodes.emailVerificationAccountUnavailable]: 'invalid',
  [identityProblemCodes.emailVerificationResendRateLimited]: 'rate_limited',
} satisfies Record<VerifyEmailProblemCode, 'expired' | 'already_used' | 'invalid' | 'rate_limited'>;

interface ProblemData {
  code?: string | null;
  errors?: Record<string, string[]>;
  errorCodes?: Record<string, string[]>;
}

export function getProblemCode(error: ApiError): string | null {
  const data = asProblemData(error.data);
  return typeof data?.code === 'string' && data.code.length > 0 ? data.code : null;
}

export function classifySignInError(error: ApiError): SignInErrorKind {
  if (error.status === 429) return 'rate_limited';

  const code = getProblemCode(error);
  return isSignInProblemCode(code) ? signInProblemKinds[code] : 'form';
}

export function getSignInCopyKey(kind: SignInSubmitErrorKind): TranslationKey {
  return signInCopyKeys[kind];
}

export function getSignInFieldError(
  errorData: ProblemData,
  field: 'email' | 'password',
): LocalizedErrorDescriptor | null {
  const code = getFirstFieldErrorCode(errorData, field);
  const key = code ? signInFieldCopyKeys[code] : undefined;
  return key ? { key } : null;
}

export function getRegisterFieldError(
  errorData: ProblemData,
  field: 'fullName' | 'email' | 'password' | 'passwordConfirmation' | 'acceptedTerms',
): LocalizedErrorDescriptor | null {
  const code =
    getFirstFieldErrorCode(errorData, field) ??
    (field === 'acceptedTerms'
      ? (getFirstFieldErrorCode(errorData, 'acceptedTermsVersion') ??
        getFirstFieldErrorCode(errorData, 'acceptedPrivacyVersion'))
      : undefined);
  const key = code ? registerFieldCopyKeys[code] : undefined;
  return key ? { key } : null;
}

export function getRegisterProblemFieldError(
  error: ApiError,
): { field: 'email'; error: LocalizedErrorDescriptor } | null {
  return getProblemCode(error) === identityProblemCodes.registerEmailAlreadyExists
    ? { field: 'email', error: { key: 'auth.registerDuplicateEmail' } }
    : null;
}

export function classifyVerifyEmailError(
  error: ApiError,
): 'expired' | 'already_used' | 'invalid' | 'rate_limited' {
  if (error.status === 429) return 'rate_limited';

  const code = getProblemCode(error);
  return isVerifyEmailProblemCode(code) ? verifyEmailProblemKinds[code] : 'invalid';
}

export function getFirstFieldError(errorData: ProblemData, field: string): string | undefined {
  return errorData.errors?.[field]?.[0];
}

function getFirstFieldErrorCode(errorData: ProblemData, field: string): string | undefined {
  return errorData.errorCodes?.[field]?.[0];
}

function isSignInProblemCode(code: string | null): code is SignInProblemCode {
  return (
    code === identityProblemCodes.signInInvalidCredentials ||
    code === identityProblemCodes.signInVerificationRequired ||
    code === identityProblemCodes.signInAccountUnavailable
  );
}

function isVerifyEmailProblemCode(code: string | null): code is VerifyEmailProblemCode {
  return (
    code === identityProblemCodes.emailVerificationInvalidToken ||
    code === identityProblemCodes.emailVerificationExpiredToken ||
    code === identityProblemCodes.emailVerificationAlreadyUsedToken ||
    code === identityProblemCodes.emailVerificationAccountUnavailable ||
    code === identityProblemCodes.emailVerificationResendRateLimited
  );
}

function asProblemData(data: unknown): ProblemData | null {
  return typeof data === 'object' && data !== null ? (data as ProblemData) : null;
}
