import { SignInPage } from '../identitySignIn/SignInPage'
import { TwoStepPage } from '../identitySignIn/TwoStepPage'
import { identityInvalid, identitySignInContext, identityTwoStepTexts } from './fixtures'

export type IdentitySignInPage = 'signIn' | 'wrongPassword' | 'organizationOnly' | 'twoStep'

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
      // Two-step verification is a preview-only state until the theme draws it.
      return (
        <TwoStepPage
          texts={identityTwoStepTexts}
          organization={identitySignInContext.realmName}
          locales={identitySignInContext.locales}
        />
      )
  }
}
