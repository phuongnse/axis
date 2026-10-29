import type { IdentityContext } from '../src/identitySignIn/signInContext'

/** Reads the context that login.ftl or login-otp.ftl writes as JSON into `#axis-sign-in-context`. */
export function readSignInContext(document: Document): IdentityContext {
  const element = document.getElementById('axis-sign-in-context')
  if (!element) {
    throw new Error('The sign-in context #axis-sign-in-context is missing.')
  }
  return JSON.parse(element.textContent) as IdentityContext
}
