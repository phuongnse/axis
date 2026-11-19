import { apiFetch } from './api'

/** How one source of notices reaches the person, as served by `GET /api/me/notification-preferences`. */
export interface NotificationSource {
  channels: { application: boolean; email: boolean }
  /** Whether the source's emails wait for the daily digest. Meaningful while `channels.email` is on. */
  digest: boolean
}

/** The sources of notices that a person chooses for. */
export type SourceName = 'task' | 'mention' | 'process' | 'authoringRequest'

/** The signed-in person's notification preferences, see docs/reference/notifications.md#notification-preferences. */
export interface OwnNotificationPreferences {
  sources: Record<SourceName, NotificationSource>
  /** The hour of the day, 0 to 23, at which the daily digest is sent. */
  digestHour: number
  /** The IANA time zone of the digest hour. */
  timeZone: string
}

/** Loads the signed-in person's notification preferences. A 401 shows the sign-in prompt. */
export async function fetchOwnNotificationPreferences(signal?: AbortSignal): Promise<OwnNotificationPreferences> {
  const response = await apiFetch('/api/me/notification-preferences', { signal })
  if (!response.ok) {
    throw new Error(`Loading the notification preferences failed with status ${response.status}.`)
  }
  return (await response.json()) as OwnNotificationPreferences
}

/** Replaces the person's notification preferences with the whole set. Answers the status: 204 when saved. */
export async function replaceOwnNotificationPreferences(preferences: OwnNotificationPreferences): Promise<number> {
  const response = await apiFetch('/api/me/notification-preferences', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(preferences),
  })
  return response.status
}
