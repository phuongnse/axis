import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { OwnAuthenticators } from '../platform/ownAuthenticators'
import type { OwnNotificationPreferences } from '../platform/ownNotificationPreferences'
import type { OwnSessions } from '../platform/ownSessions'
import { assignLocation } from '../platform/navigation'
import { chooseOption } from '../test/antd'
import { TextProvider } from '../platform/TextProvider'
import { AccountPage } from './AccountPage'
import { PlatformContext, type Platform } from './context'

vi.mock('../platform/navigation')

const platform: Platform = {
  site: {
    name: 'platform',
    titleKey: 'shell.title',
    locales: { default: 'en', fallback: 'en', available: ['en'] },
    navigation: [],
  },
  fallbackTexts: platformEnglish,
  current: { locale: 'en', texts: platformEnglish },
  setCurrent: () => {},
  development: false,
}

const sessions = (endAllowed: boolean): OwnSessions => ({
  endAllowed,
  sessions: [
    { id: 'current-id', signedInAt: '2026-10-28T09:00:00Z', lastRequestAt: '2026-10-28T09:30:00Z', current: true },
    { id: 'other id/1', signedInAt: '2026-10-27T08:00:00Z', lastRequestAt: '2026-10-27T08:10:00Z', current: false },
  ],
})

const authenticator = (id: string, label: string | null = 'Phone') => ({
  id,
  label,
  createdAt: '2026-10-20T10:00:00Z',
})

const authenticatorsOf = (...ids: string[]): OwnAuthenticators => ({
  account: true,
  authenticators: ids.map((id) => authenticator(id)),
})

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const preferencesBody = (): OwnNotificationPreferences => ({
  sources: {
    task: { channels: { application: true, email: true }, digest: false },
    mention: { channels: { application: true, email: true }, digest: false },
    process: { channels: { application: true, email: true }, digest: false },
    authoringRequest: { channels: { application: true, email: true }, digest: false },
  },
  digestHour: 8,
  timeZone: 'UTC',
})

/** What the stub answers for the notification preferences and `GET /api/me`, and the bodies that `PUT` received. */
interface NotificationsStub {
  /** The `users` of `GET /api/me`. */
  users?: boolean
  /** The status `PUT` answers. */
  replaceStatus?: number
  load?: () => Response
  bodies?: unknown[]
}

/**
 * Stubs `fetch` with the list, which answers `list()`, the end routes, which answer `end()`, and the
 * authenticators, which answer `ownAuthenticators()`. `notifications` sets the preferences and `GET /api/me`.
 */
function stubServer(
  list: () => Response,
  end: () => Response = () => new Response(null, { status: 204 }),
  ownAuthenticators: () => Response = () => json(authenticatorsOf('totp-1')),
  notifications: NotificationsStub = {},
) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === '/api/me') {
      return json({ users: notifications.users ?? false })
    }
    if (input === '/api/me/notification-preferences') {
      if (init?.method === 'PUT') {
        notifications.bodies?.push(JSON.parse(init.body as string))
        return new Response(null, { status: notifications.replaceStatus ?? 204 })
      }
      return (notifications.load ?? (() => json(preferencesBody())))()
    }
    if (input === '/api/me/authenticators') {
      return ownAuthenticators()
    }
    if (input === '/api/me/sessions' && (init?.method ?? 'GET') === 'GET') {
      return list()
    }
    if (input.startsWith('/api/me/sessions') && init?.method === 'DELETE') {
      return end()
    }
    return new Response(null, { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function deleteCalls(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'DELETE')
}

function renderPage(address = '/account') {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter initialEntries={[address]}>
          <AccountPage />
        </MemoryRouter>
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

beforeEach(() => {
  vi.mocked(assignLocation).mockReset()
})

describe('AccountPage', () => {
  it('disables every end button and offers Sign in again when the session may not end sessions', async () => {
    const fetchMock = stubServer(() => json(sessions(false)))
    renderPage()

    const endButtons = await screen.findAllByRole('button', { name: 'End' })
    expect(endButtons).toHaveLength(1)
    expect(endButtons[0]).toBeDisabled()
    expect(screen.getByRole('button', { name: 'End all other sessions' })).toBeDisabled()

    await userEvent.click(screen.getByRole('button', { name: 'Sign in again' }))

    expect(assignLocation).toHaveBeenCalledExactlyOnceWith('/api/auth/sign-in?returnUrl=%2Faccount&reauthenticate=true')
    expect(deleteCalls(fetchMock)).toHaveLength(0)
  })

  it('marks the current session and gives it no End button', async () => {
    stubServer(() => json(sessions(true)))
    renderPage()

    const current = (await screen.findByText('This session')).closest('tr')!

    expect(within(current).getByText('This session').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')

    expect(within(current).queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Sign in again' })).not.toBeInTheDocument()
  })

  it('ends one session with the CSRF header, then loads the list again', async () => {
    const fetchMock = stubServer(() => json(sessions(true)))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'End' }))

    await waitFor(() => expect(deleteCalls(fetchMock)).toHaveLength(1))
    const [input, init] = deleteCalls(fetchMock)[0]
    expect(input).toBe('/api/me/sessions/other%20id%2F1')
    expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.filter(([input, init]) => input === '/api/me/sessions' && !init?.method),
      ).toHaveLength(2),
    )
  })

  it('ends all other sessions, then loads the list again', async () => {
    const fetchMock = stubServer(() => json(sessions(true)))
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'End all other sessions' }))

    await waitFor(() => expect(deleteCalls(fetchMock)).toHaveLength(1))
    const [input, init] = deleteCalls(fetchMock)[0]
    expect(input).toBe('/api/me/sessions')
    expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
    await waitFor(() =>
      expect(
        fetchMock.mock.calls.filter(([input, init]) => input === '/api/me/sessions' && !init?.method),
      ).toHaveLength(2),
    )
  })

  it('loads the list again after a 403, which now shows Sign in again', async () => {
    let allowed = true
    stubServer(
      () => json(sessions(allowed)),
      () => {
        allowed = false
        return json({ title: 'Sign in again.' }, 403)
      },
    )
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'End' }))

    expect(await screen.findByRole('button', { name: 'Sign in again' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'End' })).toBeDisabled()
    expect(screen.queryByTestId('sessions-end-error')).not.toBeInTheDocument()
  })

  it('shows an error when ending a session fails with another status', async () => {
    stubServer(
      () => json(sessions(true)),
      () => new Response(null, { status: 500 }),
    )
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'End' }))

    expect(await screen.findByText('The session could not be ended.')).toBeInTheDocument()
  })

  it('disables End all other sessions when the current session is the only one', async () => {
    stubServer(() => json({ endAllowed: true, sessions: [sessions(true).sessions[0]] }))
    renderPage()

    expect(await screen.findByRole('button', { name: 'End all other sessions' })).toBeDisabled()
  })

  it('shows an error when the list cannot be loaded', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(() => new Response(null, { status: 500 }))
    renderPage()

    expect(await screen.findByText('Your sessions could not be loaded.')).toBeInTheDocument()
    warn.mockRestore()
  })

  it('disables Remove on the only authenticator and enables it on each of two', async () => {
    stubServer(
      () => json(sessions(true)),
      undefined,
      () => json(authenticatorsOf('totp-1')),
    )
    renderPage()

    expect(await screen.findByRole('button', { name: 'Remove' })).toBeDisabled()
  })

  it('enables Remove when the person has two authenticators', async () => {
    stubServer(
      () => json(sessions(true)),
      undefined,
      () => json(authenticatorsOf('totp-1', 'totp-2')),
    )
    renderPage()

    const buttons = await screen.findAllByRole('button', { name: 'Remove' })
    expect(buttons).toHaveLength(2)
    buttons.forEach((button) => expect(button).toBeEnabled())
  })

  it('sends Change password, Add and Remove to the identity provider through the sign-in route', async () => {
    stubServer(
      () => json(sessions(true)),
      undefined,
      () => json(authenticatorsOf('totp-1', 'totp-2')),
    )
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Change password' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))
    await userEvent.click(screen.getAllByRole('button', { name: 'Remove' })[1])

    expect(vi.mocked(assignLocation).mock.calls).toEqual([
      ['/api/auth/sign-in?returnUrl=%2Faccount&action=UPDATE_PASSWORD'],
      ['/api/auth/sign-in?returnUrl=%2Faccount&action=CONFIGURE_TOTP'],
      ['/api/auth/sign-in?returnUrl=%2Faccount&action=delete_credential%3Atotp-2'],
    ])
  })

  it('shows neither action to a user who is not an account, but still lists the sessions', async () => {
    stubServer(
      () => json(sessions(true)),
      undefined,
      () => json({ account: false, authenticators: [] }),
    )
    renderPage()

    expect(await screen.findByText('This session')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Change password' })).not.toBeInTheDocument())
    expect(screen.queryByText('Authenticators')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add' })).not.toBeInTheDocument()
  })

  it('shows the em dash for an authenticator with no added date', async () => {
    stubServer(
      () => json(sessions(true)),
      undefined,
      () => json({ account: true, authenticators: [{ id: 'totp-1', label: 'Phone', createdAt: null }] }),
    )
    renderPage()

    const row = (await screen.findByText('Phone')).closest('tr')!

    expect(within(row).getByText('—')).toBeVisible()
  })

  it('shows an error and no actions when the authenticators cannot be loaded', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(
      () => json(sessions(true)),
      undefined,
      () => new Response(null, { status: 500 }),
    )
    renderPage()

    expect(await screen.findByTestId('authenticators-error')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Change password' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add' })).not.toBeInTheDocument()
    warn.mockRestore()
  })

  it('shows Password, Authenticators and Active sessions as parts with a rule between them', async () => {
    stubServer(() => json(sessions(true)))
    renderPage()

    for (const name of ['Password', 'Authenticators', 'Active sessions']) {
      const region = await screen.findByRole('region', { name })
      expect(within(region).getByRole('heading', { level: 2, name })).toBeVisible()
    }
    expect(screen.getAllByRole('separator')).toHaveLength(2)
  })

  it('shows only Active sessions, with no rule, to a user who is not an account', async () => {
    stubServer(
      () => json(sessions(true)),
      undefined,
      () => json({ account: false, authenticators: [] }),
    )
    renderPage()

    const region = await screen.findByRole('region', { name: 'Active sessions' })
    expect(within(region).getByRole('heading', { level: 2 })).toBeVisible()
    await waitFor(() => expect(screen.getAllByRole('region')).toHaveLength(1))
    expect(screen.queryByRole('separator')).not.toBeInTheDocument()
  })

  it('narrows the sessions by a filter in the address, with no new request for the list', async () => {
    const day = dayjs('2026-10-27T08:00:00Z').format('YYYY-MM-DD')
    const filter = encodeURIComponent(JSON.stringify({ all: [{ field: 'signedInAt', op: 'eq', value: day }] }))
    const fetchMock = stubServer(() => json(sessions(true)))
    renderPage(`/account?sessions.filter=${filter}`)

    expect(await screen.findAllByRole('button', { name: 'End' })).toHaveLength(1)
    expect(screen.queryByText('This session')).not.toBeInTheDocument()
    expect(
      fetchMock.mock.calls.filter(([input, init]) => input === '/api/me/sessions' && (init?.method ?? 'GET') === 'GET'),
    ).toHaveLength(1)
  })

  describe('Notifications tab', () => {
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone

    async function openNotifications(notifications: NotificationsStub = {}) {
      const stub = stubServer(() => json(sessions(true)), undefined, undefined, notifications)
      renderPage()
      await userEvent.click(await screen.findByRole('tab', { name: 'Notifications' }))
      return stub
    }

    async function choose(name: string, option: string) {
      await userEvent.click(await screen.findByRole('combobox', { name }))
      await chooseOption(option)
    }

    function savedAnnouncement() {
      return screen.getAllByRole('status').find((region) => region.textContent === 'Saved.')
    }

    it('opens Security first, with Password, Authenticators and Active sessions', async () => {
      stubServer(() => json(sessions(true)))
      renderPage()

      expect(await screen.findByRole('tab', { name: 'Security', selected: true })).toBeVisible()
      for (const name of ['Password', 'Authenticators', 'Active sessions']) {
        expect(await screen.findByRole('region', { name })).toBeVisible()
      }
      expect(screen.getByRole('heading', { level: 1, name: 'Your account' })).toBeVisible()
    })

    it('shows a row for each source, each control named by its row and column', async () => {
      await openNotifications()

      for (const source of ['Tasks', 'Mentions', 'Process messages', 'AI requests']) {
        expect(await screen.findByRole('switch', { name: `${source}: In the application` })).toBeChecked()
        expect(screen.getByRole('combobox', { name: `${source}: Email` })).toBeVisible()
      }
      expect(screen.queryByText('Daily digest at')).not.toBeInTheDocument()
    })

    it('sends the whole changed preferences at once for a switch and then for an email choice, and announces Saved.', async () => {
      const bodies: unknown[] = []
      await openNotifications({ bodies })

      await userEvent.click(await screen.findByRole('switch', { name: 'Mentions: In the application' }))

      await waitFor(() => expect(savedAnnouncement()).toBeDefined())
      const first = preferencesBody()
      first.sources.mention.channels.application = false
      first.timeZone = zone
      expect(bodies).toEqual([first])
      expect(screen.getByRole('switch', { name: 'Mentions: In the application' })).not.toBeChecked()

      await choose('Tasks: Email', 'Off')

      await waitFor(() => expect(bodies).toHaveLength(2))
      const second = structuredClone(first)
      second.sources.task.channels.email = false
      expect(bodies[1]).toEqual(second)
      await waitFor(() => expect(savedAnnouncement()).toBeDefined())
    })

    it('keeps the digest when Off is chosen, and sets both flags for the daily digest', async () => {
      const bodies: unknown[] = []
      await openNotifications({ bodies })

      await choose('Process messages: Email', 'In the daily digest')
      await waitFor(() => expect(bodies).toHaveLength(1))
      expect((bodies[0] as OwnNotificationPreferences).sources.process).toEqual({
        channels: { application: true, email: true },
        digest: true,
      })

      await waitFor(() => expect(screen.getByRole('combobox', { name: 'Process messages: Email' })).toBeEnabled())
      await choose('Process messages: Email', 'Off')
      await waitFor(() => expect(bodies).toHaveLength(2))
      expect((bodies[1] as OwnNotificationPreferences).sources.process).toEqual({
        channels: { application: true, email: false },
        digest: true,
      })
    })

    it('puts the last value back and shows an alert when the save is refused', async () => {
      const bodies: unknown[] = []
      await openNotifications({ bodies, replaceStatus: 500 })

      await userEvent.click(await screen.findByRole('switch', { name: 'Mentions: In the application' }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Your change could not be saved, so it was put back.')
      expect(screen.getByRole('switch', { name: 'Mentions: In the application' })).toBeChecked()
      expect(savedAnnouncement()).toBeUndefined()
      expect(bodies).toHaveLength(1)
    })

    it('puts the last email choice back when the save is refused', async () => {
      await openNotifications({ replaceStatus: 400 })

      await choose('Tasks: Email', 'Off')

      expect(await screen.findByRole('alert')).toHaveTextContent('it was put back')
      expect(screen.getByRole('combobox', { name: 'Tasks: Email' })).toBeEnabled()
      expect(screen.getByRole('combobox', { name: 'Tasks: Email' }).closest('.ant-select')).toHaveTextContent(
        'Right away',
      )
    })

    it('disables the other controls while a save runs, and shows only the changed one busy', async () => {
      let finish: (response: Response) => void = () => {}
      const fetchMock = await openNotifications()
      const answer = fetchMock.getMockImplementation()!
      fetchMock.mockImplementation(async (input, init) =>
        init?.method === 'PUT' ? new Promise<Response>((resolve) => (finish = resolve)) : answer(input, init),
      )

      await userEvent.click(await screen.findByRole('switch', { name: 'Mentions: In the application' }))

      await waitFor(() => expect(screen.getByRole('switch', { name: 'Tasks: In the application' })).toBeDisabled())
      expect(screen.getByRole('combobox', { name: 'Mentions: Email' })).toBeDisabled()
      expect(screen.getByRole('switch', { name: 'Mentions: In the application' })).toHaveClass('ant-switch-loading')

      finish(new Response(null, { status: 204 }))

      await waitFor(() => expect(screen.getByRole('switch', { name: 'Tasks: In the application' })).toBeEnabled())
    })

    it('shows the administrators row, always on and with no email choice, only when users is true', async () => {
      await openNotifications({ users: true })

      const row = (await screen.findByText('Notices to administrators')).closest('tr')!
      const always = within(row).getByRole('switch', { name: 'Notices to administrators: In the application' })
      expect(always).toBeChecked()
      expect(always).toBeDisabled()
      expect(within(row).getByText('Always on')).toBeVisible()
      expect(within(row).queryByRole('combobox')).not.toBeInTheDocument()
    })

    it('shows no administrators row when users is false', async () => {
      await openNotifications({ users: false })

      expect(await screen.findByRole('switch', { name: 'Tasks: In the application' })).toBeVisible()
      expect(screen.queryByText('Notices to administrators')).not.toBeInTheDocument()
      expect(screen.queryByText('Always on')).not.toBeInTheDocument()
    })

    it('shows Daily digest at only while a source is in the daily digest, and saves the hour at once', async () => {
      const bodies: unknown[] = []
      await openNotifications({ bodies })
      expect(screen.queryByText('Daily digest at')).not.toBeInTheDocument()

      await choose('Tasks: Email', 'In the daily digest')

      const hour = await screen.findByRole('combobox', { name: 'Daily digest at' })
      expect(hour).toHaveAccessibleDescription(expect.stringContaining(zone.replaceAll('_', ' ')))
      expect(screen.getByText('8:00 AM')).toBeVisible()
      await waitFor(() => expect(hour).toBeEnabled())

      await choose('Daily digest at', '6:00 AM')

      await waitFor(() => expect(bodies).toHaveLength(2))
      expect(bodies[1]).toMatchObject({ digestHour: 6, timeZone: zone })

      await waitFor(() => expect(screen.getByRole('combobox', { name: 'Tasks: Email' })).toBeEnabled())
      await choose('Tasks: Email', 'Right away')
      await waitFor(() => expect(screen.queryByText('Daily digest at')).not.toBeInTheDocument())
    })

    it('shows an error and no table when the preferences cannot be loaded', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
      await openNotifications({ load: () => new Response(null, { status: 500 }) })

      expect(await screen.findByRole('alert')).toHaveTextContent('Your notification choices could not be loaded.')
      expect(screen.queryByRole('switch')).not.toBeInTheDocument()
      warn.mockRestore()
    })
  })
})
