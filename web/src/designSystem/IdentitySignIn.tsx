import { AddAuthenticatorPage } from '../identitySignIn/AddAuthenticatorPage'
import { ErrorPage } from '../identitySignIn/ErrorPage'
import { NoticePage } from '../identitySignIn/NoticePage'
import { ResetPasswordPage } from '../identitySignIn/ResetPasswordPage'
import { SelectAuthenticatorPage } from '../identitySignIn/SelectAuthenticatorPage'
import { SignInPage } from '../identitySignIn/SignInPage'
import { SignOutPage } from '../identitySignIn/SignOutPage'
import { TermsPage } from '../identitySignIn/TermsPage'
import { TwoStepPage } from '../identitySignIn/TwoStepPage'
import { UpdatePasswordPage } from '../identitySignIn/UpdatePasswordPage'
import { UpdateProfilePage } from '../identitySignIn/UpdateProfilePage'
import {
  identityAddAuthenticatorContext,
  identityErrorContext,
  identityInvalid,
  identityNoticeContext,
  identityResetPasswordContext,
  identitySelectAuthenticatorContext,
  identitySignInContext,
  identitySignOutContext,
  identityTermsContext,
  identityTwoStepContext,
  identityUpdatePasswordContext,
  identityUpdateProfileContext,
} from './fixtures'

export type IdentitySignInPage =
  | 'signIn'
  | 'wrongPassword'
  | 'fieldsEmpty'
  | 'organizationOnly'
  | 'twoStep'
  | 'resetPassword'
  | 'updatePassword'
  | 'addAuthenticator'
  | 'selectAuthenticator'
  | 'updateProfile'
  | 'terms'
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
    case 'fieldsEmpty':
      return <SignInPage context={{ ...identitySignInContext, username: '' }} />
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
    case 'selectAuthenticator':
      return <SelectAuthenticatorPage context={identitySelectAuthenticatorContext} />
    case 'updateProfile':
      return <UpdateProfilePage context={identityUpdateProfileContext} />
    case 'terms':
      return <TermsPage context={identityTermsContext} />
    case 'signOut':
      return <SignOutPage context={identitySignOutContext} />
    case 'notice':
      return <NoticePage context={identityNoticeContext} />
    case 'error':
      return <ErrorPage context={identityErrorContext} />
  }
}
