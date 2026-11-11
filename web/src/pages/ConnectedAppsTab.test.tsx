import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import type { TenantOutsideClients } from '../platform/tenantOutsideClients'
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

/** Answers a PUT with `status` and a read with `stored`. */
function stubServer(status: number, stored: TenantOutsideClients) {
  const fetchMock = vi.fn(async (_input: string, init?: RequestInit) =>
    init?.method === 'PUT' ? new Response(null, { status }) : json(stored),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function puts(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'PUT')
}

function renderTab(initial: TenantOutsideClients) {
  return render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <ConnectedAppsTab initial={initial} />
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

const none: TenantOutsideClients = { outsideClients: 'none', allowedClients: [] }

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
    const fetchMock = stubServer(204, { outsideClients: 'allowList', allowedClients: ['claude', 'stored-by-server'] })
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
    const fetchMock = stubServer(204, { outsideClients: 'none', allowedClients: ['claude'] })
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
    stubServer(500, none)
    renderTab(none)

    await allowClient(user, 'claude')
    await user.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('The setting was not saved.')).toBeVisible()
    expect(screen.getByRole('radio', { name: 'Allow list' })).toBeChecked()
    expect(screen.getByText('claude')).toBeVisible()
  })

  it('says that the change is stored but not applied when the save answers 502', async () => {
    const user = userEvent.setup()
    stubServer(502, none)
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
})
