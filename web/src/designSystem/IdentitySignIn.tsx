import { AddAuthenticatorPage } from '../identitySignIn/AddAuthenticatorPage'
import { ResetPasswordPage } from '../identitySignIn/ResetPasswordPage'
import { SignInPage } from '../identitySignIn/SignInPage'
import { TermsPage } from '../identitySignIn/TermsPage'
import { TwoStepPage } from '../identitySignIn/TwoStepPage'
import { UpdatePasswordPage } from '../identitySignIn/UpdatePasswordPage'
import {
  identityAddAuthenticatorContext,
  identityInvalid,
  identityResetPasswordContext,
  identityTermsContext,
  identitySignInContext,
  identityTwoStepContext,
  identityUpdatePasswordContext,
} from './fixtures'

export type IdentitySignInPage =
  | 'signIn'
  | 'emptyFields'
  | 'wrongPassword'
  | 'organizationOnly'
  | 'twoStep'
  | 'resetPassword'
  | 'updatePassword'
  | 'addAuthenticator'
  | 'terms'

/** The identity provider's sign-in page in one of its states, built from fixtures through the shared sign-in components. */
export function IdentitySignIn({ page }: { page: IdentitySignInPage }) {
  switch (page) {
    case 'signIn':
      return <SignInPage context={identitySignInContext} />
    case 'emptyFields':
      return <SignInPage context={{ ...identitySignInContext, username: '' }} />
    case 'wrongPassword':
      return <SignInPage context={{ ...identitySignInContext, error: identityInvalid }} />
    case 'organizationOnly':
      return <SignInPage context={{ ...identitySignInContext, accounts: false }} />
    case 'twoStep':
      return <TwoStepPage context={identityTwoStepContext} />
    case 'resetPassword':
      return <ResetPasswordPage context={identityResetPasswordContext} />
    case 'updatePassword':
      return <UpdatePasswordPage context={identityUpdatePasswordContext} />
    case 'addAuthenticator':
      return <AddAuthenticatorPage context={identityAddAuthenticatorContext} />
    case 'terms':
      return <TermsPage context={identityTermsContext} />
  }
}
