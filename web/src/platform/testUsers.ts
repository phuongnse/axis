/** A development test user, as served by `GET /api/me` and `GET /api/test-users`. */
export interface TestUser {
  id: string
  displayName: string
  roles: string[]
}

const jsonHeaders = { 'Content-Type': 'application/json' }

/** Loads the configured test users, or returns `null` when the server does not offer them. */
export async function fetchTestUsers(signal?: AbortSignal): Promise<TestUser[] | null> {
  const response = await fetch('/api/test-users', { signal })
  if (!response.ok) {
    return null
  }
  const body = (await response.json()) as { users: TestUser[] }
  return body.users
}

/** Loads the signed-in user, or returns `null` when nobody is signed in. */
export async function fetchCurrentUser(signal?: AbortSignal): Promise<TestUser | null> {
  const response = await fetch('/api/me', { signal })
  if (response.status === 401) {
    return null
  }
  if (!response.ok) {
    throw new Error(`Loading the current user failed with status ${response.status}.`)
  }
  return (await response.json()) as TestUser
}

export async function signIn(id: string): Promise<void> {
  const response = await fetch('/api/test-users/sign-in', {
    method: 'POST',
    headers: jsonHeaders,
    body: JSON.stringify({ id }),
  })
  if (!response.ok) {
    throw new Error(`Signing in failed with status ${response.status}.`)
  }
}

// The server answers 415 to a sign-out without a JSON body.
export async function signOut(): Promise<void> {
  const response = await fetch('/api/test-users/sign-out', { method: 'POST', headers: jsonHeaders, body: '{}' })
  if (!response.ok) {
    throw new Error(`Signing out failed with status ${response.status}.`)
  }
}
