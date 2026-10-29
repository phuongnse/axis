import { StrictMode } from 'react'
import { AddAuthenticatorPage } from '../src/identitySignIn/AddAuthenticatorPage'
import { SignInPage } from '../src/identitySignIn/SignInPage'
import type { IdentityContext } from '../src/identitySignIn/signInContext'
import { TwoStepPage } from '../src/identitySignIn/TwoStepPage'
import { ThemeModeProvider } from '../src/platform/theme'

/** The theme's page: the shared sign-in, two-step or add-an-authenticator page with the SPA's theme provider, fed Keycloak's context. */
export function SignInEntry({ context }: { context: IdentityContext }) {
  return (
    <StrictMode>
      <ThemeModeProvider>{drawPage(context)}</ThemeModeProvider>
    </StrictMode>
  )
}

function drawPage(context: IdentityContext) {
  switch (context.page) {
    case 'twoStep':
      return <TwoStepPage context={context} />
    case 'addAuthenticator':
      return <AddAuthenticatorPage context={context} />
    default:
      return <SignInPage context={context} />
  }
}
