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

/** The person's connected AI. The key itself is never answered, only its last four characters. */
export interface OwnAiConnection {
  provider: string
  ending: string
}

/**
 * Proposed: `GET /api/me/ai`, which no API answers yet (#3926). The providers a person may connect, and the person's
 * connected AI, or `null`.
 */
export interface OwnAi {
  providers: OwnAiProvider[]
  connection: OwnAiConnection | null
}

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
 * server stores it. `true` when the server answers `204`. The prototype sends the key unsealed.
 */
export async function connectOwnAi(provider: string, key: string): Promise<boolean> {
  const response = await apiFetch('/api/me/ai', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ provider, sealedKey: key }),
  })
  return response.status === 204
}
