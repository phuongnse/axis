import { apiFetch } from './api'

/** One live session of the signed-in person, as served by `GET /api/me/sessions`. */
export interface OwnSession {
  id: string
  /** When the session started, as an ISO 8601 time. */
  signedInAt: string
  /** When the session last made a request, as an ISO 8601 time. */
  lastRequestAt: string
  /** Whether this session made the request. */
  current: boolean
}

/** The person's live sessions, newest sign-in first. */
export interface OwnSessions {
  /** Whether the session may end sessions now: the last login is at most 5 minutes old. */
  endAllowed: boolean
  sessions: OwnSession[]
}

/** Loads the signed-in person's live sessions. A 401 shows the sign-in prompt. */
export async function fetchOwnSessions(signal?: AbortSignal): Promise<OwnSessions> {
  const response = await apiFetch('/api/me/sessions', { signal })
  if (!response.ok) {
    throw new Error(`Loading the sessions failed with status ${response.status}.`)
  }
  return (await response.json()) as OwnSessions
}

/** Ends one of the person's sessions. Answers the status: 204 when ended, 403 when the last login is too old. */
export async function endSession(id: string): Promise<number> {
  const response = await apiFetch(`/api/me/sessions/${encodeURIComponent(id)}`, { method: 'DELETE' })
  return response.status
}

/** Ends every session of the person except the current one. Answers the status like {@link endSession}. */
export async function endOtherSessions(): Promise<number> {
  const response = await apiFetch('/api/me/sessions', { method: 'DELETE' })
  return response.status
}
