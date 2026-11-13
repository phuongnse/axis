import { apiFetch } from './api'
import { siteAddress } from './siteLinks'

/** The page a notification opens, by its declared name, with the raw page parameters. */
export interface NotificationOpen {
  page: string
  parameters: Record<string, unknown>
}

/**
 * Proposed: what a notification asks of its reader: to do something, only to know something, or to know that something
 * failed. See the review on #3263.
 */
export type NotificationKind = 'action' | 'info' | 'problem'

/**
 * Proposed: which notifications the bell's list shows: all of them, the unread ones, or the `action` ones whose thing
 * still waits. See the review on #3263.
 */
export type NotificationFilter = 'all' | 'unread' | 'needsAction'

/** One notification as the list route serves it, with its texts rendered. */
export interface NotificationItem {
  id: string
  /** Proposed: where it comes from, the application's title or the workspace's, in the reader's locale. */
  source: string
  /** Proposed: what it asks of its reader. */
  kind: NotificationKind
  /**
   * Proposed: whether the thing an `action` notification asks for has been dealt with, which reading it does not do.
   * Always false for another kind.
   */
  done: boolean
  /** The release that sent the notification, or `null` for a tenant notice. */
  releaseId: string | null
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

// The bell sits in the header of every page, so a 401 never shows the sign-in prompt from it.
const quiet = { promptSignInOnUnauthorized: false }

/** Loads page `page` of the notifications at `path`, newest first. */
export async function fetchNotifications(
  path: string,
  page: number,
  pageSize: number,
  signal?: AbortSignal,
  filter: NotificationFilter = 'all',
): Promise<NotificationPage> {
  const only = filterQuery(filter)
  const response = await apiFetch(
    `${path}?page=${page}&pageSize=${pageSize}${only ? `&${only}` : ''}`,
    { signal },
    quiet,
  )
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
 * for each parameter that is not null. A platform notice has no site, so `sitePath` is `null` and its page is a
 * platform page at the root, such as `studio`.
 */
export function notificationAddress(sitePath: string | null, open: NotificationOpen): string {
  const page = open.page.toLowerCase()
  const path = sitePath === null ? `/${page}` : siteAddress(sitePath, page)
  const query = new URLSearchParams()
  for (const [name, value] of Object.entries(open.parameters)) {
    if (value !== null && value !== undefined) {
      query.append(name, String(value))
    }
  }
  const search = query.toString()
  return search ? `${path}?${search}` : path
}

/** The query that limits a list or a clear-all to what a filter shows. */
function filterQuery(filter: NotificationFilter): string {
  return filter === 'unread' ? 'unread=true' : filter === 'needsAction' ? 'needsAction=true' : ''
}

/** Proposed: clears one notification from the signed-in user's list, see the review on #3263. */
export async function clearNotification(path: string, id: string): Promise<void> {
  const response = await apiFetch(`${path}/${encodeURIComponent(id)}/clear`, { method: 'POST' }, quiet)
  if (!response.ok) {
    throw new Error(`Clearing a notification failed with ${response.status}`)
  }
}

/** Proposed: clears every notification the filter shows from the signed-in user's list, see the review on #3263. */
export async function clearNotifications(path: string, filter: NotificationFilter): Promise<void> {
  const query = filterQuery(filter)
  const response = await apiFetch(`${path}/clear-all${query ? `?${query}` : ''}`, { method: 'POST' }, quiet)
  if (!response.ok) {
    throw new Error(`Clearing the notifications failed with ${response.status}`)
  }
}
