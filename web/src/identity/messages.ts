// Proposed: the texts of Axis's identity provider pages. The Keycloak theme built from these pages would ship them as
// its message bundles, in every locale the platform offers.
export const identityMessages = {
  en: {
    signInTitle: 'Sign in',
    continueWith: 'Continue with {0}',
    or: 'or',
    username: 'Email or username',
    password: 'Password',
    forgotPassword: 'Forgot password?',
    rememberMe: 'Keep me signed in',
    signIn: 'Sign in',
    invalidCredentials: 'Invalid email, username or password.',
    organizationOnly: 'Your organization signs you in with {0}.',
    otpTitle: 'Two-step verification',
    otpHint: 'Enter the 6-digit code from your authenticator app.',
    otpLabel: 'Verification code',
    verify: 'Verify',
    tryAnotherWay: 'Try another way',
    back: 'Back to sign in',
    english: 'English',
    vietnamese: 'Tiếng Việt',
  },
} as const

export type IdentityMessages = (typeof identityMessages)['en']

/** A message with `{0}` replaced, as Keycloak's message bundles write their parameters. */
export function format(message: string, value: string): string {
  return message.replace('{0}', value)
}
