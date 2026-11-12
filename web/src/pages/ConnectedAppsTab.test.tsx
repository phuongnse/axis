import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { TenantConnectedApp, TenantOutsideClients } from '../platform/tenantOutsideClients'
import { ConnectedAppsTab } from './ConnectedAppsTab'
import { PlatformContext, type Platform } from './context'

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

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

interface Server {
  /** The status of a PUT of the setting. */
  put: number
  stored: TenantOutsideClients
  /** The answers to the reads of the list, one for each read, the last one repeating. */
  apps: (() => Response)[]
  /** The status of a revoke. */
  revoke: number
}

const noApps = () => json({ items: [] })

/** Routes a request by its path and method. */
function stubServer(server: Partial<Server> = {}) {
  const { put = 204, stored = none, apps = [noApps], revoke = 204 } = server
  let reads = 0
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === '/api/tenant/connected-apps') {
      return apps[Math.min(reads++, apps.length - 1)]()
    }
    if (input.endsWith('/revoke')) {
      return new Response(null, { status: revoke })
    }
    return init?.method === 'PUT' ? new Response(null, { status: put }) : json(stored)
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function puts(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT')
}

function appReads(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([input]) => input === '/api/tenant/connected-apps')
}

const draftHelper: TenantConnectedApp = {
  clientId: 'claude/desktop',
  name: 'Draft helper',
  lastUsedAt: '2026-03-04T10:15:00Z',
  people: [{ userId: 'u1', displayName: 'Mai Tran', email: 'mai@example.test', consentedAt: '2026-03-01T08:00:00Z' }],
}

/** Opens the Revoke dialog of the row of `clientId`. */
async function openRevoke(user: ReturnType<typeof userEvent.setup>, clientId: string) {
  const row = (await screen.findByText(clientId)).closest('tr')!
  await user.click(within(row).getByRole('button', { name: 'Revoke…' }))
  return screen.findByRole('dialog')
}

const none: TenantOutsideClients = { outsideClients: 'none', allowedClients: [] }

function renderTab(initial: TenantOutsideClients) {
  return render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ConnectedAppsTab initial={initial} />
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

/** Chooses Allow list and adds `clientId` to the list. */
async function allowClient(user: ReturnType<typeof userEvent.setup>, clientId: string) {
  await user.click(screen.getByRole('radio', { name: 'Allow list' }))
  await user.type(await screen.findByRole('combobox', { name: 'Allowed client IDs' }), `${clientId}{Enter}`)
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('ConnectedAppsTab', () => {
  it('shows the stored mode, and the list of client ids only under Allow list', async () => {
    const user = userEvent.setup()
    renderTab({ outsideClients: 'allowList', allowedClients: ['claude'] })

    expect(screen.getByRole('radio', { name: 'Allow list' })).toBeChecked()
    expect(screen.getByText('claude')).toBeVisible()
    await user.click(screen.getByRole('radio', { name: 'None' }))
    expect(screen.queryByRole('combobox', { name: 'Allowed client IDs' })).toBeNull()
  })

  it('sends both values, then reads them again and shows what the server stored', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer({
      stored: { outsideClients: 'allowList', allowedClients: ['claude', 'stored-by-server'] },
    })
    renderTab(none)

    await allowClient(user, 'claude')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The setting was saved.')).toBeVisible()
    expect(puts(fetchMock)).toHaveLength(1)
    const [input, init] = puts(fetchMock)[0]
    expect(input).toBe('/api/tenant/outside-clients')
    expect(JSON.parse(init?.body as string)).toEqual({ outsideClients: 'allowList', allowedClients: ['claude'] })
    expect(fetchMock).toHaveBeenLastCalledWith('/api/tenant/outside-clients', expect.anything())
    expect(screen.getByText('claude')).toBeVisible()
    expect(screen.getByText('stored-by-server')).toBeVisible()
  })

  it('sends the list under every mode', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer({ stored: { outsideClients: 'none', allowedClients: ['claude'] } })
    renderTab({ outsideClients: 'allowList', allowedClients: ['claude'] })

    await user.click(screen.getByRole('radio', { name: 'None' }))
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The setting was saved.')).toBeVisible()
    expect(JSON.parse(puts(fetchMock)[0][1]?.body as string)).toEqual({
      outsideClients: 'none',
      allowedClients: ['claude'],
    })
  })

  it('shows the not-saved text and keeps the chosen mode and the edited ids when the save answers 500', async () => {
    const user = userEvent.setup()
    stubServer({ put: 500 })
    renderTab(none)

    await allowClient(user, 'claude')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The setting was not saved.')).toBeVisible()
    expect(screen.getByRole('radio', { name: 'Allow list' })).toBeChecked()
    expect(screen.getByText('claude')).toBeVisible()
  })

  it('says that the change is stored but not applied when the save answers 502', async () => {
    const user = userEvent.setup()
    stubServer({ put: 502 })
    renderTab(none)

    await allowClient(user, 'claude')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(
      await screen.findByText(
        'The setting was stored, but the identity provider did not apply it. Save again to apply it.',
      ),
    ).toBeVisible()
    expect(screen.getByRole('radio', { name: 'Allow list' })).toBeChecked()
    expect(screen.getByText('claude')).toBeVisible()
  })

  it('shows the not-saved text when the request throws', async () => {
    const user = userEvent.setup()
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        throw new TypeError('network')
      }),
    )
    renderTab(none)

    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The setting was not saved.')).toBeVisible()
    expect(warn).toHaveBeenCalled()
  })

  it('lists each client with its name, client ID, people and last use, and a client with no name shows the em dash', async () => {
    stubServer({
      apps: [
        () =>
          json({
            items: [draftHelper, { clientId: 'unnamed', name: null, lastUsedAt: null, people: [] }],
          }),
      ],
    })
    renderTab(none)

    const row = (await screen.findByText('claude/desktop')).closest('tr')!
    expect(within(row).getByText('Draft helper')).toBeVisible()
    expect(within(row).getByText('Mai Tran')).toBeVisible()
    expect(within(row).getByText('mai@example.test')).toBeVisible()
    expect(within(row).getByText(new Date('2026-03-04T10:15:00Z').toLocaleString('en'))).toBeVisible()
    const unnamed = screen.getByText('unnamed').closest('tr')!
    expect(within(unnamed).getAllByText('—')).toHaveLength(2)
  })

  it('says that no app has consent when the list is empty', async () => {
    stubServer()
    renderTab(none)

    expect(
      await screen.findByText('No app has consent yet. People give consent when they connect an MCP client.'),
    ).toBeVisible()
  })

  it('names the client in the confirmation, posts the revoke to its encoded path and loads the list again', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer({ apps: [() => json({ items: [draftHelper] }), noApps] })
    renderTab(none)

    const dialog = await openRevoke(user, 'claude/desktop')
    expect(within(dialog).getByText('Draft helper')).toBeVisible()
    await user.click(within(dialog).getByRole('button', { name: 'Revoke' }))

    await waitFor(() => expect(screen.queryByText('claude/desktop')).toBeNull())
    const revokes = fetchMock.mock.calls.filter(([input]) => String(input).endsWith('/revoke'))
    expect(revokes).toHaveLength(1)
    expect(revokes[0][0]).toBe('/api/tenant/connected-apps/claude%2Fdesktop/revoke')
    expect(revokes[0][1]?.method).toBe('POST')
    expect(appReads(fetchMock)).toHaveLength(2)
  })

  it('names a client with no name by its client ID in the confirmation', async () => {
    const user = userEvent.setup()
    stubServer({ apps: [() => json({ items: [{ ...draftHelper, name: null }] })] })
    renderTab(none)

    const dialog = await openRevoke(user, 'claude/desktop')

    expect(within(dialog).getByText('claude/desktop')).toBeVisible()
  })

  it('shows the not-revoked text and keeps the row when the revoke answers 500', async () => {
    const user = userEvent.setup()
    const fetchMock = stubServer({ apps: [() => json({ items: [draftHelper] })], revoke: 500 })
    renderTab(none)

    const dialog = await openRevoke(user, 'claude/desktop')
    await user.click(within(dialog).getByRole('button', { name: 'Revoke' }))

    expect(await screen.findByText('The consent was not revoked.')).toBeVisible()
    expect(screen.getByText('claude/desktop')).toBeVisible()
    expect(appReads(fetchMock)).toHaveLength(1)
  })

  it('says that the revoke is not finished and keeps the row when the revoke answers 502', async () => {
    const user = userEvent.setup()
    stubServer({ apps: [() => json({ items: [draftHelper] })], revoke: 502 })
    renderTab(none)

    const dialog = await openRevoke(user, 'claude/desktop')
    await user.click(within(dialog).getByRole('button', { name: 'Revoke' }))

    expect(
      await screen.findByText('The identity provider did not finish the revoke. Revoke again to finish it.'),
    ).toBeVisible()
    expect(screen.getByText('claude/desktop')).toBeVisible()
  })

  it('shows the not-revoked text when the revoke request throws', async () => {
    const user = userEvent.setup()
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => {
        if (input === '/api/tenant/connected-apps') {
          return json({ items: [draftHelper] })
        }
        throw new TypeError('network')
      }),
    )
    renderTab(none)

    const dialog = await openRevoke(user, 'claude/desktop')
    await user.click(within(dialog).getByRole('button', { name: 'Revoke' }))

    expect(await screen.findByText('The consent was not revoked.')).toBeVisible()
    expect(warn).toHaveBeenCalled()
  })

  it('shows the load-failed text when the list answers 502, and the switch still saves', async () => {
    const user = userEvent.setup()
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer({ apps: [() => new Response(null, { status: 502 })] })
    renderTab(none)

    expect(await screen.findByText('The apps with consent could not be loaded.')).toBeVisible()
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The setting was saved.')).toBeVisible()
    expect(puts(fetchMock)).toHaveLength(1)
  })
})
