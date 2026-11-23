import { apiFetch } from './api'

/**
 * A provider a person may connect, one for each binding of the installation that needs a key. The connection names no
 * model: the author chooses one for each request in Studio's request panel, which #5204 designs.
 */
export interface OwnAiProvider {
  id: string
  /** The provider's name, such as Anthropic. */
  name: string
}

/**
 * Proposed (#3926): what the provider answered the last time it saw the key, at the connect's test or at a request.
 * `working` once it accepted the key, `refused` when it refused the key itself (`401` or `403`), and `billing` when the
 * account behind the key could not pay (`402`, a spend limit's `400` or a spend cap's `429`). A provider that is down
 * or overloaded leaves the state as it was, since that says nothing about the key.
 */
export type OwnAiState = 'working' | 'refused' | 'billing'

/** The person's connected AI. The key itself is never answered, only its last four characters. */
export interface OwnAiConnection {
  provider: string
  ending: string
  state: OwnAiState
}

/**
 * Proposed: `GET /api/me/ai`, which no API answers yet (#3926). The providers a person may connect, and the person's
 * connected AI, or `null`.
 */
export interface OwnAi {
  providers: OwnAiProvider[]
  connection: OwnAiConnection | null
}

/**
 * Proposed (#3926): why a connect failed, from the problem `reason` of its `422`, as a quota refusal names its own, see
 * docs/reference/tenant-quotas.md#refusals. `failed` for any other answer.
 */
export type OwnAiConnectFailure = 'key-refused' | 'billing' | 'provider-unavailable' | 'failed'

const connectFailures: readonly OwnAiConnectFailure[] = ['key-refused', 'billing', 'provider-unavailable']

/** Loads the signed-in person's AI. A 401 shows the sign-in prompt. */
export async function fetchOwnAi(signal?: AbortSignal): Promise<OwnAi> {
  const response = await apiFetch('/api/me/ai', { signal })
  if (!response.ok) {
    throw new Error(`Loading your AI failed with status ${response.status}.`)
  }
  return (await response.json()) as OwnAi
}

/**
 * Proposed: `PUT /api/me/ai` with `{ provider, sealedKey }` (#3926). The browser seals the key for the agent host,
 * as docs/reference/keys-and-data-protection.md#key-policy says, and the agent host tests it with the provider before the
 * server stores it. `null` when the server answers `204`, else why it failed. The prototype sends the key unsealed.
 */
export async function connectOwnAi(provider: string, key: string): Promise<OwnAiConnectFailure | null> {
  const response = await apiFetch('/api/me/ai', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ provider, sealedKey: key }),
  })
  if (response.status === 204) {
    return null
  }
  if (response.status !== 422) {
    return 'failed'
  }
  try {
    const { reason } = (await response.json()) as { reason?: unknown }
    return connectFailures.find((failure) => failure === reason) ?? 'failed'
  } catch {
    return 'failed'
  }
}

/**
 * Proposed: `DELETE /api/me/ai` (#3926), which removes the person's sealed key and its data key, so no copy opens again,
 * as D149 agrees. `true` when the server answers `204`.
 */
export async function removeOwnAi(): Promise<boolean> {
  const response = await apiFetch('/api/me/ai', { method: 'DELETE' })
  return response.status === 204
}
