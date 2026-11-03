import { AddAuthenticatorPage } from '../identitySignIn/AddAuthenticatorPage'
import { ResetPasswordPage } from '../identitySignIn/ResetPasswordPage'
import { SelectAuthenticatorPage } from '../identitySignIn/SelectAuthenticatorPage'
import { SignInPage } from '../identitySignIn/SignInPage'
import { TwoStepPage } from '../identitySignIn/TwoStepPage'
import { UpdatePasswordPage } from '../identitySignIn/UpdatePasswordPage'
import { UpdateProfilePage } from '../identitySignIn/UpdateProfilePage'
import {
  identityAddAuthenticatorContext,
  identityInvalid,
  identityResetPasswordContext,
  identitySelectAuthenticatorContext,
  identitySignInContext,
  identityTwoStepContext,
  identityUpdatePasswordContext,
  identityUpdateProfileContext,
} from './fixtures'

export type IdentitySignInPage =
  | 'signIn'
  | 'wrongPassword'
  | 'organizationOnly'
  | 'twoStep'
  | 'resetPassword'
  | 'updatePassword'
  | 'addAuthenticator'
  | 'selectAuthenticator'
  | 'updateProfile'

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
    case 'selectAuthenticator':
      return <SelectAuthenticatorPage context={identitySelectAuthenticatorContext} />
    case 'updateProfile':
      return <UpdateProfilePage context={identityUpdateProfileContext} />
  }
}
