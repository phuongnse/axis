import { StrictMode } from 'react'
import { SignInPage } from '../src/identitySignIn/SignInPage'
import type { IdentityContext } from '../src/identitySignIn/signInContext'
import { TwoStepPage } from '../src/identitySignIn/TwoStepPage'
import { ThemeModeProvider } from '../src/platform/theme'

/** The theme's page: the shared sign-in or two-step page with the SPA's theme provider, fed Keycloak's context. */
export function SignInEntry({ context }: { context: IdentityContext }) {
  return (
    <StrictMode>
      <ThemeModeProvider>
        {context.page === 'twoStep' ? <TwoStepPage context={context} /> : <SignInPage context={context} />}
      </ThemeModeProvider>
    </StrictMode>
  )
}
