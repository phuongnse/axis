import { apiFetch } from './api'

/** The page a notification opens, by its declared name, with the raw page parameters. */
export interface NotificationOpen {
  page: string
  parameters: Record<string, unknown>
}

/** One notification as the list route serves it, with its texts rendered. */
export interface NotificationItem {
  id: string
  releaseId: string
  subject: string
  body: string
  open: NotificationOpen | null
  read: boolean
  createdAt: string
}

/** One page of the notifications the signed-in user sees, with the number unread across every page. */
export interface NotificationPage {
  items: NotificationItem[]
  page: number
  pageSize: number
  totalCount: number
  unreadCount: number
}

// The bell sits in the header of every page, so a 401 never starts sign-in from it.
const quiet = { signInOnUnauthorized: false }

/** Loads page `page` of the notifications at `path`, newest first. */
export async function fetchNotifications(
  path: string,
  page: number,
  pageSize: number,
  signal?: AbortSignal,
): Promise<NotificationPage> {
  const response = await apiFetch(`${path}?page=${page}&pageSize=${pageSize}`, { signal }, quiet)
  if (!response.ok) {
    throw new Error(`Loading the notifications failed with ${response.status}`)
  }
  return (await response.json()) as NotificationPage
}

/** Marks one notification read for the signed-in user. */
export async function markNotificationRead(path: string, id: string): Promise<void> {
  const response = await apiFetch(`${path}/${encodeURIComponent(id)}/read`, { method: 'POST' }, quiet)
  if (!response.ok) {
    throw new Error(`Marking a notification read failed with ${response.status}`)
  }
}

/** Marks every notification the signed-in user sees read. */
export async function markAllNotificationsRead(path: string): Promise<void> {
  const response = await apiFetch(`${path}/read-all`, { method: 'POST' }, quiet)
  if (!response.ok) {
    throw new Error(`Marking the notifications read failed with ${response.status}`)
  }
}

/**
 * The address of the page a notification opens: the page's path in the site, with one query entry
 * for each parameter that is not null.
 */
export function notificationAddress(sitePath: string, open: NotificationOpen): string {
  const path = `/${sitePath}/${open.page.toLowerCase()}`
  const query = new URLSearchParams()
  for (const [name, value] of Object.entries(open.parameters)) {
    if (value !== null && value !== undefined) {
      query.append(name, String(value))
    }
  }
  const search = query.toString()
  return search ? `${path}?${search}` : path
}
