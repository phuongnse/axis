import { useSyncExternalStore } from 'react'

/** Whether a call answered 401. Kept apart from `api.ts` so the test setup can reset it without loading the navigation. */
let signInNeeded = false
const signInListeners = new Set<() => void>()

/** Whether a call answered 401, so the sign-in prompt shows. It stays set until the browser leaves the page. */
export function isSignInNeeded(): boolean {
  return signInNeeded
}

/** Calls `listener` when sign-in becomes needed. Returns a function that removes it. */
export function subscribeSignInNeeded(listener: () => void): () => void {
  signInListeners.add(listener)
  return () => signInListeners.delete(listener)
}

/** Clears the sign-in state. For tests. */
export function resetSignInNeeded(): void {
  signInNeeded = false
}

export function requireSignIn(): void {
  if (signInNeeded) {
    return
  }
  signInNeeded = true
  signInListeners.forEach((listener) => listener())
}

/** Whether a call answered 401, so the sign-in prompt shows. */
export function useSignInNeeded(): boolean {
  return useSyncExternalStore(subscribeSignInNeeded, isSignInNeeded)
}
