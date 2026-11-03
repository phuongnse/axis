export interface SignInProvider {
  alias: string
  /** The provider's name, such as "Contoso ID". */
  label: string
  loginUrl: string
}

export interface SignInLocale {
  /** The language tag, such as `en`. */
  tag: string
  /** The language's name in its own language, such as "Tiếng Việt". */
  label: string
  /** Where the page opens in this language. */
  url: string
}

/** The texts of the frame's "Language and theme" gear. */
export interface FrameTexts {
  languageAndTheme: string
  language: string
  theme: string
  system: string
  light: string
  dark: string
}

/** What every page of the identity provider shares: the organization's name and the "Language and theme" gear's languages and texts. */
export interface IdentityFrameContext {
  /** The realm's display name. Absent when the realm has none or it equals the realm id, so the heading names Axis. */
  realmName?: string
  /** The tag of the page's language. */
  locale: string
  locales: SignInLocale[]
  frameTexts: FrameTexts
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
  /** The button that cancels an application-initiated action. */
  cancel: string
}

/** The texts of the page that adds an authenticator. */
export interface AddAuthenticatorTexts {
  title: string
  message: string
  /** The alternative text of the QR code image. */
  qrCode: string
  /** The line above the key to type by hand. */
  manualKey: string
  code: string
  codeRequired: string
  deviceName: string
  deviceNameRequired: string
  submit: string
  /** The button that cancels an application-initiated action. */
  cancel: string
}

/** What the site that started the sign-in allows. */
export interface SignInSite {
  /** The aliases of the providers the site allows. */
  providers: string[]
  /** Whether the site allows accounts of the realm. */
  accounts: boolean
}

/** What the identity provider's sign-in page draws. The design system feeds it fixtures, the theme feeds it Keycloak's. */
export interface SignInContext extends IdentityFrameContext {
  page: 'signIn'
  title: string
  providers: SignInProvider[]
  /** Whether people may sign in with an account of the realm, next to or instead of the providers. */
  accounts: boolean
  /** The providers and accounts that the site of the sign-in allows. Absent for a client that belongs to no site, which shows all. */
  site?: SignInSite
  username?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Keycloak's message that is not an error, such as the notice after "Send link". It shows above the form. */
  notice?: string
  loginAction: string
  /** Whether the "Keep me signed in" checkbox shows. */
  rememberMe: boolean
  forgotPasswordUrl?: string
  texts: SignInTexts
}

/** What the two-step verification page draws: the 6-digit code that follows the password. */
export interface TwoStepContext extends IdentityFrameContext {
  page: 'twoStep'
  title: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the code form posts `otp` and `login`, and where "Try another way" posts `tryAnotherWay=on`. */
  loginAction: string
  /** Where "Back to sign in" restarts the sign-in. */
  restartUrl: string
  /** Whether Keycloak offers another way to sign in, which shows "Try another way". */
  tryAnotherWay: boolean
  texts: TwoStepTexts
}

/** What the page that adds an authenticator draws: the QR code, the key to type by hand, the code and the device name. */
export interface AddAuthenticatorContext extends IdentityFrameContext {
  page: 'addAuthenticator'
  title: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the form posts `totp`, `totpSecret`, `userLabel` and `mode`. */
  loginAction: string
  /** The QR code as a base64 PNG. */
  qrCode: string
  /** The secret in groups, for a person to type by hand. */
  manualKey: string
  /** The secret that the form posts back as `totpSecret`. */
  secret: string
  /** Keycloak's mode, `qr` or `manual`, which the form posts back when Keycloak gave one. */
  mode?: string
  /** Whether the person already has an authenticator, so that the device name is required. */
  deviceNameRequired: boolean
  /** Whether an application started this step, such as the account page, which shows Cancel. An invitation or a reset link leaves it out. */
  appInitiated?: boolean
  texts: AddAuthenticatorTexts
}

/** What the "Forgot password?" page draws: the email or username that Keycloak sends a link to. */
export interface ResetPasswordContext extends IdentityFrameContext {
  page: 'resetPassword'
  title: string
  /** The username that the person typed on the sign-in page, if any. */
  username?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the form posts `username`. */
  loginAction: string
  /** Where "Back to sign in" goes. */
  loginUrl: string
  texts: ResetPasswordTexts
}

/** What the page that sets a password draws. An invitation and a reset link both open it. */
export interface UpdatePasswordContext extends IdentityFrameContext {
  page: 'updatePassword'
  title: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the form posts `password-new`, `password-confirm` and `logout-sessions`. */
  loginAction: string
  /** Whether an application started this step, such as the account page, which shows Cancel. An invitation or a reset link leaves it out. */
  appInitiated?: boolean
  texts: UpdatePasswordTexts
}

/** What the sign-out question draws: Keycloak asks whether to end the session when the server could not do it itself. */
export interface SignOutContext extends IdentityFrameContext {
  page: 'signOut'
  title: string
  /** Where the form posts `session_code` and `confirmLogout`. */
  action: string
  /** Keycloak's code of the sign-out, which the form posts back as `session_code`. */
  sessionCode: string
  /** Where "Back to application" goes. Absent when Keycloak gives none. */
  backUrl?: string
  texts: { message: string; signOut: string; backToApplication: string }
}

/** What a notice draws, such as "Your account has been updated", with its link onward. */
export interface NoticeContext extends IdentityFrameContext {
  page: 'notice'
  title: string
  message: string
  /** Where the notice goes on, such as the application or the next step. Absent when Keycloak offers no link. */
  linkUrl?: string
  /** The text of the link. Set together with `linkUrl`. */
  linkText?: string
}

/** What an error page draws: Keycloak's message as an error alert. */
export interface ErrorContext extends IdentityFrameContext {
  page: 'error'
  title: string
  error: string
  /** Where "Back to application" goes. Absent when the client has no home address. */
  backUrl?: string
  texts: { backToApplication: string }
}

/** What the expired page draws: what happened, with Continue and Start again. */
export interface PageExpiredContext extends IdentityFrameContext {
  page: 'pageExpired'
  title: string
  /** Where Continue goes, Keycloak's login action. */
  continueUrl: string
  /** Where Start again goes, Keycloak's restart address. */
  restartUrl: string
  texts: { message: string; continue: string; startAgain: string }
}

/** What the SAML redirect draws: a short card, and a form that posts at once. */
export interface SamlPostContext extends IdentityFrameContext {
  page: 'samlPost'
  title: string
  /** Where the form posts. */
  action: string
  samlRequest?: string
  samlResponse?: string
  relayState?: string
  texts: { message: string; continue: string }
}

/** What the front-channel sign-out draws: the hidden sign-out frame of each client, then the redirect. */
export interface FrontChannelSignOutContext extends IdentityFrameContext {
  page: 'frontChannelSignOut'
  title: string
  /** The clients whose hidden frame signs them out. */
  clients: { name: string; url: string }[]
  /** Where the page goes on after the frames have loaded. Absent when Keycloak gives none. */
  redirectUrl?: string
  texts: { message: string; continue: string }
}

/** What the page of the identity provider's theme draws, told apart by `page`. */
export type IdentityContext =
  | SignInContext
  | TwoStepContext
  | AddAuthenticatorContext
  | ResetPasswordContext
  | UpdatePasswordContext
  | SignOutContext
  | NoticeContext
  | ErrorContext
  | PageExpiredContext
  | SamlPostContext
  | FrontChannelSignOutContext
