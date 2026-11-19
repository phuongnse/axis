import { apiFetch } from './api'

/**
 * The sources a person chooses for, see docs/reference/notifications.md#notification-preferences.
 * Proposed: `authoringRequest`, the notices of the person's AI requests, which the API's sources do not hold yet.
 */
export const notificationSources = ['task', 'mention', 'process', 'authoringRequest'] as const

export type NotificationSource = (typeof notificationSources)[number]

/** How one source reaches the person. */
export interface SourcePreference {
  channels: { application: boolean; email: boolean }
  /** Whether its emails wait for the daily digest. */
  digest: boolean
}

/** The signed-in person's notification preferences, as `GET /api/me/notification-preferences` answers them. */
export interface NotificationPreferences {
  sources: Record<NotificationSource, SourcePreference>
  /** The whole hour, 0 to 23, at which the digest is sent. */
  digestHour: number
  /** The IANA time zone of the digest hour. */
  timeZone: string
}

/** Loads the signed-in person's preferences. A 401 shows the sign-in prompt. */
export async function fetchNotificationPreferences(signal?: AbortSignal): Promise<NotificationPreferences> {
  const response = await apiFetch('/api/me/notification-preferences', { signal })
  if (!response.ok) {
    throw new Error(`Loading the notification preferences failed with status ${response.status}.`)
  }
  return (await response.json()) as NotificationPreferences
}

/** Replaces the signed-in person's preferences. Answers the status: 204 when saved, 400 for a refused value. */
export async function saveNotificationPreferences(preferences: NotificationPreferences): Promise<number> {
  const response = await apiFetch('/api/me/notification-preferences', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(preferences),
  })
  return response.status
}
