import { AddAuthenticatorPage } from '../identitySignIn/AddAuthenticatorPage'
import { SignInPage } from '../identitySignIn/SignInPage'
import { TwoStepPage } from '../identitySignIn/TwoStepPage'
import {
  identityAddAuthenticatorContext,
  identityInvalid,
  identitySignInContext,
  identityTwoStepContext,
} from './fixtures'

export type IdentitySignInPage = 'signIn' | 'wrongPassword' | 'organizationOnly' | 'twoStep' | 'addAuthenticator'

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
    case 'addAuthenticator':
      return <AddAuthenticatorPage context={identityAddAuthenticatorContext} />
  }
}
