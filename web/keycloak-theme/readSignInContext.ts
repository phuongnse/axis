import type { SignInContext } from '../src/identitySignIn/signInContext'

/** Reads the sign-in context that login.ftl writes as JSON into `#axis-sign-in-context`. */
export function readSignInContext(document: Document): SignInContext {
  const element = document.getElementById('axis-sign-in-context')
  if (!element) {
    throw new Error('The sign-in context #axis-sign-in-context is missing.')
  }
  return JSON.parse(element.textContent) as SignInContext
}
