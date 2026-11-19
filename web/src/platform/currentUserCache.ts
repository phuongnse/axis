import type { CurrentUser } from './currentUser'

/**
 * The page-wide load of the signed-in user, shared by every `useCurrentUser` caller. Kept apart from `currentUser.ts` so the
 * test setup can reset it without loading `api.ts` and the navigation, as `signInNeeded.ts` is kept apart from `api.ts`.
 */
let shared: Promise<CurrentUser | null> | undefined
let settled: { user: CurrentUser | null } | undefined

/** The user once the shared load has settled, or `undefined` while it has not. */
export function settledCurrentUser(): CurrentUser | null | undefined {
  return settled?.user
}

/** Starts `load` once and gives every caller its promise. A failed load is dropped, so the next caller tries again. */
export function sharedCurrentUser(load: () => Promise<CurrentUser | null>): Promise<CurrentUser | null> {
  if (shared === undefined) {
    const promise = load()
    shared = promise
    promise.then(
      (user) => {
        if (shared === promise) {
          settled = { user }
        }
      },
      () => {
        if (shared === promise) {
          shared = undefined
        }
      },
    )
  }
  return shared
}

/** Forgets the loaded user. For tests. */
export function resetCurrentUser(): void {
  shared = undefined
  settled = undefined
}
