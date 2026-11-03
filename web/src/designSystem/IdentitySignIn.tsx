import { AddAuthenticatorPage } from '../identitySignIn/AddAuthenticatorPage'
import { ErrorPage } from '../identitySignIn/ErrorPage'
import { NoticePage } from '../identitySignIn/NoticePage'
import { ResetPasswordPage } from '../identitySignIn/ResetPasswordPage'
import { SignInPage } from '../identitySignIn/SignInPage'
import { SignOutPage } from '../identitySignIn/SignOutPage'
import { TwoStepPage } from '../identitySignIn/TwoStepPage'
import { UpdatePasswordPage } from '../identitySignIn/UpdatePasswordPage'
import {
  identityAddAuthenticatorContext,
  identityErrorContext,
  identityInvalid,
  identityNoticeContext,
  identityResetPasswordContext,
  identitySignInContext,
  identitySignOutContext,
  identityTwoStepContext,
  identityUpdatePasswordContext,
} from './fixtures'

export type IdentitySignInPage =
  | 'signIn'
  | 'wrongPassword'
  | 'organizationOnly'
  | 'twoStep'
  | 'resetPassword'
  | 'updatePassword'
  | 'addAuthenticator'
  | 'signOut'
  | 'notice'
  | 'error'

/** The identity provider's sign-in page in one of its states, built from fixtures through the shared sign-in components. */
export function IdentitySignIn({ page }: { page: IdentitySignInPage }) {
  switch (page) {
    case 'signIn':
      return <SignInPage context={identitySignInContext} />
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
    case 'signOut':
      return <SignOutPage context={identitySignOutContext} />
    case 'notice':
      return <NoticePage context={identityNoticeContext} />
    case 'error':
      return <ErrorPage context={identityErrorContext} />
  }
}
