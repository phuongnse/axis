import { assignLocation } from './navigation'

const unsafeMethods = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

/** Options of {@link apiFetch}. */
export interface ApiFetchOptions {
  /** Whether a 401 sends the browser to sign in. Defaults to `true`. */
  signInOnUnauthorized?: boolean
}

/** Answers a request in the browser. See {@link setApiGuard}. */
export type ApiGuard = (input: string, init?: RequestInit) => Response | Promise<Response>

const guardedPrefix = '/api/apps/'

let guard: ApiGuard | null = null

/**
 * While set, answers every `/api/apps/` request in the browser and never sends it. Other requests
 * still go to the server. `null` clears the guard.
 */
export function setApiGuard(next: ApiGuard | null): void {
  guard = next
}

/** The server's sign-in address, which brings the browser back to the local path `returnUrl`. */
export function signInUrl(returnUrl: string): string {
  return `/api/auth/sign-in?returnUrl=${encodeURIComponent(returnUrl)}`
}

/** Sends the browser to sign in, and back to the current page with its query string after. */
export function startSignIn(): void {
  assignLocation(signInUrl(window.location.pathname + window.location.search))
}

/**
 * `fetch` for `/api`. Unsafe requests carry the CSRF header from D23, which a page on another site
 * cannot set without a CORS preflight. Other requests pass through unchanged. A 401 means the
 * session is missing or expired, so it starts sign-in unless `signInOnUnauthorized` is `false`.
 * The response comes back either way. While a guard from {@link setApiGuard} is set, an
 * `/api/apps/` request gets the guard's answer instead, without the CSRF header or the sign-in.
 */
export async function apiFetch(
  input: string,
  init?: RequestInit,
  { signInOnUnauthorized = true }: ApiFetchOptions = {},
): Promise<Response> {
  if (guard && input.startsWith(guardedPrefix)) {
    return guard(input, init)
  }
  const response = await send(input, init)
  if (response.status === 401 && signInOnUnauthorized) {
    startSignIn()
  }
  return response
}

function send(input: string, init?: RequestInit): Promise<Response> {
  if (!unsafeMethods.has((init?.method ?? 'GET').toUpperCase())) {
    return fetch(input, init)
  }
  const headers = new Headers(init?.headers)
  headers.set('X-Axis-Csrf', '1')
  return fetch(input, { ...init, headers })
}
