export interface SignInProvider {
  alias: string
  /** The provider's name, such as "Contoso ID". */
  label: string
  loginUrl: string
}

export interface SignInLocale {
  label: string
  url: string
}

/** The page's texts. `signInWith` and `providerOnly` hold a `{provider}` placeholder for the provider's label. */
export interface SignInTexts {
  signInWith: string
  providerOnly: string
  or: string
  username: string
  password: string
  usernameRequired: string
  passwordRequired: string
  forgotPassword: string
  keepSignedIn: string
  signIn: string
}

/** What the identity provider's sign-in page draws. The design system feeds it fixtures, the theme feeds it Keycloak's. */
export interface SignInContext {
  title: string
  realmName: string
  providers: SignInProvider[]
  /** Whether people may sign in with an account of the realm, next to or instead of the providers. */
  accounts: boolean
  username?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  loginAction: string
  /** Whether the "Keep me signed in" checkbox shows. */
  rememberMe: boolean
  forgotPasswordUrl?: string
  locales: SignInLocale[]
  texts: SignInTexts
}
