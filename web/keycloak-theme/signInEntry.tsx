import { StrictMode } from 'react'
import { SignInPage } from '../src/identitySignIn/SignInPage'
import type { SignInContext } from '../src/identitySignIn/signInContext'
import { ThemeModeProvider } from '../src/platform/theme'

/** The theme's page: the shared sign-in page with the SPA's theme provider, fed Keycloak's context. */
export function SignInEntry({ context }: { context: SignInContext }) {
  return (
    <StrictMode>
      <ThemeModeProvider>
        <SignInPage context={context} />
      </ThemeModeProvider>
    </StrictMode>
  )
}
