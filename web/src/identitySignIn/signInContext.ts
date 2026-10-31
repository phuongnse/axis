export interface SignInProvider {
  alias: string
  /** The provider's name, such as "Contoso ID". */
  label: string
  loginUrl: string
}

import type { LegalLinks, LegalTexts } from '../platform/LegalLine'
import type { PreferencesTexts } from '../platform/PreferencesMenu'

/** A language that the identity provider offers: its tag, such as `vi`, and the address that shows the page in it. */
export interface SignInLocale {
  tag: string
  url: string
}

/** Proposed: the legal links under the card and their names. The realm sync writes the site's or its tenant's links. */
export interface SignInLegal {
  links: LegalLinks
  texts: LegalTexts
}

/** Proposed: what the language and theme menu below the card needs: the page's language, every language offered and the menu's texts. */
export interface SignInPreferences {
  locale: string
  locales: SignInLocale[]
  texts: PreferencesTexts
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
  /** The realm's display name. Absent when the realm has none or it equals the realm id, so the heading names Axis. */
  realmName?: string
  providers: SignInProvider[]
  /** Whether people may sign in with an account of the realm, next to or instead of the providers. */
  accounts: boolean
  /** The providers and accounts that the site of the sign-in allows. Absent for a client that belongs to no site, which shows all. */
  site?: SignInSite
  username?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Proposed: a notice Keycloak brings back to the page, such as the one after "Send link", shown as a success alert. */
  notice?: string
  loginAction: string
  /** Whether the "Keep me signed in" checkbox shows. */
  rememberMe: boolean
  forgotPasswordUrl?: string
  preferences: SignInPreferences
  legal: SignInLegal
  texts: SignInTexts
}

/** What the two-step verification page draws: the 6-digit code that follows the password. */
export interface TwoStepContext {
  page: 'twoStep'
  title: string
  /** The realm's display name. Absent when the realm has none or it equals the realm id, so the heading names Axis. */
  realmName?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the code form posts `otp` and `login`, and where "Try another way" posts `tryAnotherWay=on`. */
  loginAction: string
  /** Where "Back to sign in" restarts the sign-in. */
  restartUrl: string
  /** Whether Keycloak offers another way to sign in, which shows "Try another way". */
  tryAnotherWay: boolean
  preferences: SignInPreferences
  legal: SignInLegal
  texts: TwoStepTexts
}

/** What the page that adds an authenticator draws: the QR code, the key to type by hand, the code and the device name. */
export interface AddAuthenticatorContext {
  page: 'addAuthenticator'
  title: string
  /** The realm's display name. Absent when the realm has none or it equals the realm id, so the heading names Axis. */
  realmName?: string
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
  preferences: SignInPreferences
  legal: SignInLegal
  texts: AddAuthenticatorTexts
}

/** What the "Forgot password?" page draws: the email or username that Keycloak sends a link to. */
export interface ResetPasswordContext {
  page: 'resetPassword'
  title: string
  /** The realm's display name. Absent when the realm has none or it equals the realm id, so the heading names Axis. */
  realmName?: string
  /** The username that the person typed on the sign-in page, if any. */
  username?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the form posts `username`. */
  loginAction: string
  /** Where "Back to sign in" goes. */
  loginUrl: string
  preferences: SignInPreferences
  legal: SignInLegal
  texts: ResetPasswordTexts
}

/** What the page that sets a password draws. An invitation and a reset link both open it. */
export interface UpdatePasswordContext {
  page: 'updatePassword'
  title: string
  /** The realm's display name. Absent when the realm has none or it equals the realm id, so the heading names Axis. */
  realmName?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the form posts `password-new`, `password-confirm` and `logout-sessions`. */
  loginAction: string
  preferences: SignInPreferences
  legal: SignInLegal
  texts: UpdatePasswordTexts
}

/** Proposed: the texts of the terms page. `agree*` hold `{terms}` and `{privacy}` placeholders for the two links. */
export interface TermsTexts {
  title: string
  message: string
  agreeBoth: string
  agreeTerms: string
  agreePrivacy: string
  termsLink: string
  privacyLink: string
  agreeRequired: string
  accept: string
  decline: string
}

/** Proposed: what the terms page draws: the tenant's terms and privacy policy, which the person accepts once. */
export interface TermsContext {
  page: 'terms'
  title: string
  /** The realm's display name. Absent when the realm has none or it equals the realm id, so the heading names Axis. */
  realmName?: string
  /** The message to show above the form. No error means no alert. */
  error?: string
  /** Where the form posts `accept`, or `cancel` to decline. */
  loginAction: string
  /** The tenant's terms and privacy policy, which the person accepts. The action shows only while one is set. */
  accept: { privacy?: string; terms?: string }
  preferences: SignInPreferences
  legal: SignInLegal
  texts: TermsTexts
}

/** Proposed: one way out of a message page: a link, or a form that posts its fields, such as Sign out. */
export interface MessageAction {
  kind: 'link' | 'post'
  label: string
  href: string
  /** The hidden fields that a `post` action sends. */
  fields?: Record<string, string>
  /** The page's one solid button. The others are links. */
  primary?: boolean
}

/**
 * Proposed: what a message page draws: a title, a message and its ways out. Keycloak's sign-out question, notices,
 * errors, the expired page, the front-channel sign-out and the SAML redirect all use it.
 */
export interface MessageContext {
  page: 'message'
  title: string
  /** The realm's display name. Absent when the realm has none or it equals the realm id, so the heading names Axis. */
  realmName?: string
  /** The message under the title. An error shows it as an error alert. */
  message?: string
  tone?: 'error'
  actions: MessageAction[]
  /** The front-channel sign-out addresses, each loaded in a hidden frame. */
  frames?: string[]
  /** Where the page goes once its frames have loaded. */
  redirect?: string
  /** Whether the page posts its first `post` action at once, as the SAML redirect does. */
  autoSubmit?: boolean
  preferences: SignInPreferences
  legal: SignInLegal
}

/** Proposed: one way to sign in that "Try another way" offers. */
export interface SignInMethod {
  id: string
  label: string
  help?: string
}

/** Proposed: what the page that "Try another way" opens draws: each way to sign in as a button. */
export interface ChooseMethodContext {
  page: 'chooseMethod'
  title: string
  realmName?: string
  /** Where each button posts `authenticationExecution`. */
  loginAction: string
  methods: SignInMethod[]
  preferences: SignInPreferences
  legal: SignInLegal
}

/** Proposed: one field of the person's profile, from Keycloak's user profile. */
export interface ProfileField {
  name: string
  label: string
  value: string
  required: boolean
  readOnly: boolean
  /** `email` for an email address, otherwise text. */
  type: 'text' | 'email'
  /** Keycloak's message for the field, after a refused post. */
  error?: string
}

/** Proposed: the texts of the profile page. */
export interface ProfileTexts {
  title: string
  message: string
  required: string
  submit: string
  cancel: string
}

/**
 * Proposed: what the profile page draws: the fields Keycloak asks a person to fill in or review, such as their name
 * and email, after an administrator asks for it or a brokered provider leaves one out.
 */
export interface ProfileContext {
  page: 'profile'
  title: string
  realmName?: string
  error?: string
  /** Keycloak's reminder of why the step shows, such as "You need to update your user profile", as an info alert. */
  notice?: string
  /** Where the form posts each field by its name. */
  loginAction: string
  fields: ProfileField[]
  /** Fields the form posts as they are, such as the page's locale. */
  hidden: Record<string, string>
  /** Whether the person may leave, which posts `cancel-aia=true`: only when an application asked for the step. */
  cancellable: boolean
  preferences: SignInPreferences
  legal: SignInLegal
  texts: ProfileTexts
}

/** What the page of the identity provider's theme draws, told apart by `page`. */
export type IdentityContext =
  | SignInContext
  | TwoStepContext
  | AddAuthenticatorContext
  | ResetPasswordContext
  | UpdatePasswordContext
  | TermsContext
  | MessageContext
  | ChooseMethodContext
  | ProfileContext
