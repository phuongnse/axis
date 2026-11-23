import { apiFetch } from './api'

/** A model a person may choose, from the models the authoring eval supports. */
export interface OwnAiModel {
  id: string
  /** The model's name as its provider writes it, such as Claude Sonnet 5.5. */
  name: string
}

/** A provider a person may connect, with the models it offers. */
export interface OwnAiProvider {
  id: string
  /** The provider's name, such as Anthropic. */
  name: string
  models: OwnAiModel[]
}

/** The person's connected AI. The key itself is never answered, only its last four characters. */
export interface OwnAiConnection {
  provider: string
  model: string
  ending: string
}

/**
 * Proposed: `GET /api/me/ai`, which no API answers yet (#3926). The providers a person may connect, each with the models
 * the authoring eval supports, and the person's connected AI, or `null`.
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
 * Proposed: `PUT /api/me/ai` with `{ provider, model, sealedKey }` (#3926). The browser seals the key for the agent host,
 * as docs/reference/keys-and-data-protection.md#key-policy says, and the agent host tests it with the provider before the
 * server stores it. `true` when the server answers `204`. The prototype sends the key unsealed.
 */
export async function connectOwnAi(provider: string, model: string, key: string): Promise<boolean> {
  const response = await apiFetch('/api/me/ai', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ provider, model, sealedKey: key }),
  })
  return response.status === 204
}
