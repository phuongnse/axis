import { apiFetch } from './api'

/** One authenticator of the signed-in person, as served by `GET /api/me/authenticators`. */
export interface OwnAuthenticator {
  id: string
  /** The name the person gave the authenticator, or `null` when it has none. */
  label: string | null
  /** When the authenticator was added, as an ISO 8601 time, or `null` when unknown. */
  createdAt: string | null
}

/** The person's authenticators. */
export interface OwnAuthenticators {
  /** Whether the person is an account of the tenant's realm. A user of a brokered provider is not. */
  account: boolean
  authenticators: OwnAuthenticator[]
}

/** Loads the signed-in person's authenticators. A 401 shows the sign-in prompt. */
export async function fetchOwnAuthenticators(signal?: AbortSignal): Promise<OwnAuthenticators> {
  const response = await apiFetch('/api/me/authenticators', { signal })
  if (!response.ok) {
    throw new Error(`Loading the authenticators failed with status ${response.status}.`)
  }
  return (await response.json()) as OwnAuthenticators
}
