import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import { NotificationBell } from './NotificationBell'
import { notificationAddress, type NotificationItem, type NotificationPage } from './notifications'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

const path = '/api/apps/E2eApp/notifications'
const noteId = '5b0f6c3e-83f1-4b0e-9d6c-0a0e1f9a7a11'

const withPage: NotificationItem = {
  id: '11111111-1111-4111-8111-111111111111',
  releaseId: '22222222-2222-4222-8222-222222222222',
  subject: 'Note ready',
  body: 'The note is ready.',
  open: { page: 'NoteDetail', parameters: { note: noteId } },
  read: false,
  createdAt: '2026-10-26T08:30:00Z',
}
const withoutPage: NotificationItem = {
  ...withPage,
  id: '33333333-3333-4333-8333-333333333333',
  subject: 'Heads up',
  body: 'Nothing to open.',
  open: null,
  read: true,
}

function page(items: NotificationItem[], unreadCount: number, extra: Partial<NotificationPage> = {}): NotificationPage {
  return { items, page: 1, pageSize: 10, totalCount: items.length, unreadCount, ...extra }
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Stubs the list route with `list()` and every POST with 204, and returns the fetch mock. */
function stubServer(list: () => Response) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if ((init?.method ?? 'GET') === 'POST') {
      return new Response(null, { status: 204 })
    }
    return input.startsWith(path) ? list() : new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function posts(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls
    .filter(([, init]) => init?.method === 'POST')
    .map(([url, init]) => ({ url, csrf: new Headers(init?.headers).get('X-Axis-Csrf') }))
}

function Location() {
  const location = useLocation()
  return <div data-testid="location">{location.pathname + location.search}</div>
}

function renderBell() {
  return render(
    <MemoryRouter initialEntries={['/e2e/notes']}>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <ThemeModeProvider>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <Routes>
              <Route
                path="*"
                element={
                  <>
                    <NotificationBell path={path} sitePath="e2e" locale="en" />
                    <Location />
                  </>
                }
              />
            </Routes>
          </TextProvider>
        </ThemeModeProvider>
      </ConfigProvider>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  vi.unstubAllGlobals()
})

describe('NotificationBell', () => {
  it('names the bell with the unread count and shows the count', async () => {
    stubServer(() => json(page([withPage], 2)))
    renderBell()

    const bell = await screen.findByRole('button', { name: 'Notifications, 2 unread' })
    expect(bell).toHaveTextContent('2')
    expect(within(bell).queryByRole('img')).toBeNull()
    expect(screen.getByRole('status')).toHaveTextContent('Notifications, 2 unread')
  })

  it('names the bell without a count when nothing is unread', async () => {
    stubServer(() => json(page([withoutPage], 0)))
    renderBell()

    const bell = await screen.findByRole('button', { name: 'Notifications' })
    expect(bell).not.toHaveTextContent(/\d/)
    expect(screen.getByRole('status')).toHaveTextContent('Notifications')
  })

  it('shows nothing before the list loads and when it fails', async () => {
    const fetchMock = stubServer(() => json({ title: 'Sign in' }, 401))
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    renderBell()

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(fetchMock.mock.calls.every(([, init]) => init?.method !== 'POST')).toBe(true)
  })

  it('asks for the first page of 10 and reloads when the panel opens', async () => {
    const fetchMock = stubServer(() => json(page([withPage], 1)))
    renderBell()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Note ready')).toBeInTheDocument()
    expect(within(dialog).getByText('The note is ready.')).toBeInTheDocument()
    expect(within(dialog).getByText('Unread')).toBeInTheDocument()
    const lists = fetchMock.mock.calls.map(([url]) => url).filter((url) => url.startsWith(path))
    expect(lists).toEqual([`${path}?page=1&pageSize=10`, `${path}?page=1&pageSize=10`])
  })

  it('posts read-all with the CSRF header and clears the count', async () => {
    let unread = 2
    const fetchMock = stubServer(() => json(page([withPage], unread)))
    const post = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input, init) => {
      if (init?.method === 'POST') {
        unread = 0
      }
      return post(input, init)
    })
    renderBell()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Notifications, 2 unread' }))
    await user.click(await screen.findByRole('button', { name: 'Mark all read' }))

    const bell = await screen.findByRole('button', { name: 'Notifications' })
    await waitFor(() => expect(bell).not.toHaveTextContent(/\d/))
    expect(posts(fetchMock)).toEqual([{ url: `${path}/read-all`, csrf: '1' }])
  })

  it('opens the page of a row with its parameters and marks it read', async () => {
    const fetchMock = stubServer(() => json(page([withPage], 1)))
    renderBell()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }))
    await user.click(await screen.findByRole('button', { name: /Note ready/ }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(`/e2e/notedetail?note=${noteId}`))
    expect(posts(fetchMock)).toEqual([{ url: `${path}/${withPage.id}/read`, csrf: '1' }])
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('only marks a row without a page read', async () => {
    const fetchMock = stubServer(() => json(page([withoutPage], 0)))
    renderBell()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Notifications' }))
    await user.click(await screen.findByRole('button', { name: /Heads up/ }))

    await waitFor(() => expect(posts(fetchMock)).toEqual([{ url: `${path}/${withoutPage.id}/read`, csrf: '1' }]))
    expect(screen.getByTestId('location')).toHaveTextContent('/e2e/notes')
  })

  it('still opens the page when marking the row read fails', async () => {
    const fetchMock = stubServer(() => json(page([withPage], 1)))
    const base = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input, init) =>
      init?.method === 'POST' ? json({ title: 'Failed' }, 500) : base(input, init),
    )
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    renderBell()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }))
    await user.click(await screen.findByRole('button', { name: /Note ready/ }))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(`/e2e/notedetail?note=${noteId}`))
  })

  it('pages the list when it holds more than 10 notifications', async () => {
    const fetchMock = stubServer(() => json(page([withPage], 1, { totalCount: 12 })))
    renderBell()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Notifications, 1 unread' }))
    await user.click(await screen.findByTitle('2'))

    await waitFor(() => expect(fetchMock.mock.calls.map(([url]) => url)).toContain(`${path}?page=2&pageSize=10`))
  })

  it('says so when there are no notifications', async () => {
    stubServer(() => json(page([], 0)))
    renderBell()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Notifications' }))

    expect(await screen.findByText('No notifications yet.')).toBeInTheDocument()
  })

  it('has Vietnamese texts for every key it uses', () => {
    const keys = Object.keys(platformEnglish).filter((key) => key.startsWith('shell.notifications.'))
    expect(keys).toHaveLength(7)
    for (const key of keys) {
      expect(platformVietnamese).toHaveProperty([key])
    }
  })
})

describe('notificationAddress', () => {
  it('opens the page in lower case with one query entry for each parameter', () => {
    expect(
      notificationAddress('e2e', { page: 'NoteDetail', parameters: { note: noteId, count: 3, done: false } }),
    ).toBe(`/e2e/notedetail?note=${noteId}&count=3&done=false`)
  })

  it('leaves out null parameters and the query when none is left', () => {
    expect(notificationAddress('e2e', { page: 'Notes', parameters: { note: null } })).toBe('/e2e/notes')
    expect(notificationAddress('e2e', { page: 'Notes', parameters: {} })).toBe('/e2e/notes')
  })
})
