import { StrictMode } from 'react'
import { AddAuthenticatorPage } from '../src/identitySignIn/AddAuthenticatorPage'
import { ResetPasswordPage } from '../src/identitySignIn/ResetPasswordPage'
import { SelectAuthenticatorPage } from '../src/identitySignIn/SelectAuthenticatorPage'
import { SignInPage } from '../src/identitySignIn/SignInPage'
import type { IdentityContext } from '../src/identitySignIn/signInContext'
import { TwoStepPage } from '../src/identitySignIn/TwoStepPage'
import { UpdatePasswordPage } from '../src/identitySignIn/UpdatePasswordPage'
import { UpdateProfilePage } from '../src/identitySignIn/UpdateProfilePage'
import { ThemeModeProvider } from '../src/platform/theme'

function ThemePage({ context }: { context: IdentityContext }) {
  switch (context.page) {
    case 'signIn':
      return <SignInPage context={context} />
    case 'twoStep':
      return <TwoStepPage context={context} />
    case 'addAuthenticator':
      return <AddAuthenticatorPage context={context} />
    case 'resetPassword':
      return <ResetPasswordPage context={context} />
    case 'updatePassword':
      return <UpdatePasswordPage context={context} />
    case 'selectAuthenticator':
      return <SelectAuthenticatorPage context={context} />
    case 'updateProfile':
    case 'reviewProfile':
      return <UpdateProfilePage context={context} />
  }
}

/** The theme's page: the shared sign-in, two-step, add-an-authenticator, forgot-password, set-password, method choice, update profile or review profile page with the SPA's theme provider, fed Keycloak's context. */
export function SignInEntry({ context }: { context: IdentityContext }) {
  return (
    <StrictMode>
      <ThemeModeProvider>
        <ThemePage context={context} />
      </ThemeModeProvider>
    </StrictMode>
  )
}
