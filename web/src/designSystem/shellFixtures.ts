import type { CurrentUser } from '../platform/currentUser'
import type { NotificationPage } from '../platform/notifications'
import type { NavigationItem, SiteListItem, SiteMetadata } from '../platform/site'
import { application, sitePath } from './fixtures'

/** Whether the shell previews draw a signed-in user or nobody signed in. */
export type PreviewSession = 'signedIn' | 'signedOut'

// The session of the preview this frame draws. The artifact draws each card in a frame of its own, so a card
// has one session. The harness, which mounts every preview on one page, draws them all in the last one's.
let session: PreviewSession = 'signedIn'

export function setPreviewSession(next: PreviewSession): void {
  session = next
}

const unauthorized = () => new Response(null, { status: 401 })

export const notificationsPath = `/api/apps/${application}/notifications`

/** The platform paths the shell previews answer, besides the fixture application's own. */
export const shellScopes = ['/api/me', '/api/sites', '/api/about', '/health/ready'] as const

const user: CurrentUser = {
  id: 'f2a4c6e8-1b3d-4f5a-8c7e-9d0b2a4c6e8f',
  displayName: 'Linh Tran',
  roles: ['requester', 'approver'],
  studio: true,
  studioPublisher: false,
  studioCreate: false,
  studioPreviewHost: null,
  studioTest: false,
  console: true,
}

const purchasing = { name: 'Purchasing', titles: { en: 'Purchasing' } }
const people = { name: 'People', titles: { en: 'People' } }
const serviceDesk = { name: 'ServiceDesk', titles: { en: 'Service desk' } }

const sites: SiteListItem[] = [
  { path: sitePath, titleKey: 'preview.site.title', titles: { en: 'Purchase requests' }, application: purchasing },
  { path: 'leave', titleKey: 'leave.title', titles: { en: 'Leave requests' }, application: people },
  { path: 'agents', titleKey: 'agents.title', titles: { en: 'Agent desk' }, application: serviceDesk },
  { path: 'help', titleKey: 'help.title', titles: { en: 'Help portal' }, application: serviceDesk },
]

const notifications: NotificationPage = {
  items: [
    {
      id: '5c7e9a1b-3d5f-4a6c-8e0b-2d4f6a8c0e1b',
      releaseId: '3d2c1b0a-9f8e-4d7c-8b6a-5f4e3d2c1b0a',
      subject: 'Standing desks for the new office was approved',
      body: 'Minh Pham approved the request.',
      open: null,
      read: false,
      createdAt: '2026-03-18T09:30:00Z',
    },
  ],
  page: 1,
  pageSize: 10,
  totalCount: 1,
  unreadCount: 1,
}

/** The answers of the platform APIs the shell reads: the user, the site list, readiness and the notifications. */
export const shellRoutes: Record<string, () => unknown> = {
  '/api/me': () => (session === 'signedIn' ? user : unauthorized()),
  '/api/sites': () => (session === 'signedIn' ? { sites } : unauthorized()),
  '/api/about': () => (session === 'signedIn' ? { version: '0.1.0' } : unauthorized()),
  '/health/ready': () => new Response('Healthy', { status: 200 }),
  [notificationsPath]: () => notifications,
}

/** The platform site, as `GET /api/site` serves it. */
export const platformSite: SiteMetadata = {
  name: 'platform',
  titleKey: 'shell.title',
  locales: { default: 'en', fallback: 'en', available: ['en', 'vi'] },
  navigation: [{ key: 'home', path: '/', labelKey: 'shell.nav.home', icon: 'home' }],
}

/** The fixture application site's navigation, as the site route builds it from its pages. */
export const siteNavigation: NavigationItem[] = [
  { key: 'Dashboard', path: `/${sitePath}/dashboard`, labelKey: 'preview.dashboard', icon: 'dashboard' },
  { key: 'Requests', path: `/${sitePath}/requests`, labelKey: 'preview.requests', icon: 'cart' },
  { key: 'Tasks', path: `/${sitePath}/tasks`, labelKey: 'preview.tasks', icon: 'tasks' },
  { key: 'Categories', path: `/${sitePath}/categories`, labelKey: 'preview.categories', icon: 'tag' },
  { key: 'Instances', path: `/${sitePath}/instances`, labelKey: 'preview.instances', icon: 'process' },
]
