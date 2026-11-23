import { apiFetch } from './api'

/**
 * A provider a person may connect, one for each binding of the installation that needs a key. The binding names the
 * model, so the person chooses no model, see docs/reference/model-bindings.md#bindings.
 */
export interface OwnAiProvider {
  id: string
  /** The provider's name, such as Anthropic. */
  name: string
  /** The name of the model the binding names, as its provider writes it, such as Claude Sonnet 5.5. */
  model: string
}

/** The person's connected AI. The key itself is never answered, only its last four characters. */
export interface OwnAiConnection {
  provider: string
  ending: string
}

/**
 * Proposed: `GET /api/me/ai`, which no API answers yet (#3926). The providers a person may connect, each with the model its
 * binding names, and the person's connected AI, or `null`.
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
