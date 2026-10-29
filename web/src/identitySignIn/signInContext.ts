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

/** The page's texts. `continueWith` and `providerOnly` hold a `{provider}` placeholder for the provider's label. */
export interface SignInTexts {
  continueWith: string
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

/** The texts of the two-step verification page. */
export interface TwoStepTexts {
  title: string
  message: string
  code: string
  verify: string
  tryAnotherWay: string
  backToSignIn: string
}

/** The texts of the "Forgot password?" page. */
export interface ResetPasswordTexts {
  title: string
  message: string
  username: string
  usernameRequired: string
  sendLink: string
  backToSignIn: string
}

/** The texts of the page that sets a password. */
export interface UpdatePasswordTexts {
  title: string
  message: string
  newPassword: string
  confirmPassword: string
  newPasswordRequired: string
  passwordsDiffer: string
  setPassword: string
}

/** What the site that started the sign-in allows. */
export interface SignInSite {
  /** The aliases of the providers the site allows. */
  providers: string[]
  /** Whether the site allows accounts of the realm. */
  accounts: boolean
}

/** What the identity provider's sign-in page draws. The design system feeds it fixtures, the theme feeds it Keycloak's. */
export interface SignInContext {
  page: 'signIn'
  title: string
  realmName: string
  providers: SignInProvider[]
  /** Whether people may sign in with an account of the realm, next to or instead of the providers. */
  accounts: boolean
  /** The providers and accounts that the site of the sign-in allows. Absent for a client that belongs to no site, which shows all. */
  site?: SignInSite
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

/** What the two-step verification page draws: the 6-digit code that follows the password. */
export interface TwoStepContext {
  page: 'twoStep'
  title: string
  realmName: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the code form posts `otp` and `login`, and where "Try another way" posts `tryAnotherWay=on`. */
  loginAction: string
  /** Where "Back to sign in" restarts the sign-in. */
  restartUrl: string
  /** Whether Keycloak offers another way to sign in, which shows "Try another way". */
  tryAnotherWay: boolean
  locales: SignInLocale[]
  texts: TwoStepTexts
}

/** What the "Forgot password?" page draws: the email or username that Keycloak sends a link to. */
export interface ResetPasswordContext {
  page: 'resetPassword'
  title: string
  realmName: string
  /** The username that the person typed on the sign-in page, if any. */
  username?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the form posts `username`. */
  loginAction: string
  /** Where "Back to sign in" goes. */
  loginUrl: string
  locales: SignInLocale[]
  texts: ResetPasswordTexts
}

/** What the page that sets a password draws. An invitation and a reset link both open it. */
export interface UpdatePasswordContext {
  page: 'updatePassword'
  title: string
  realmName: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the form posts `password-new`, `password-confirm` and `logout-sessions`. */
  loginAction: string
  locales: SignInLocale[]
  texts: UpdatePasswordTexts
}

/** What the page of the identity provider's theme draws, told apart by `page`. */
export type IdentityContext = SignInContext | TwoStepContext | ResetPasswordContext | UpdatePasswordContext
