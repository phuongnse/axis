import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { CellHostRequest, CellHostRequestState } from '../platform/cell'
import { TextProvider } from '../platform/TextProvider'
import { PlatformContext, type Platform } from './context'
import { TenantHostsSection } from './TenantHostsSection'

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

const expiresAt = '2026-12-01T10:00:00Z'
const expiry = new Date(expiresAt).toLocaleString('en')

const added: CellHostRequest = {
  host: 'portal.example.com',
  token: 'token',
  state: 'pending',
  expiresAt,
  txt: { name: '_axis-challenge.portal.example.com', value: 'axis-verify=abc' },
  cname: { name: 'portal.example.com', target: 'cell.axis.example' },
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** Answers `GET /api/tenant/hosts` with `list` and `POST /api/tenant/hosts` with `post`. */
function stubServer(list: Response | (() => Response), post: Response = json(added, 201)) {
  const fetchMock = vi.fn(async (_input: string, init?: RequestInit) =>
    init?.method === 'POST' ? post.clone() : typeof list === 'function' ? list() : list.clone(),
  )
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function posts(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
}

function renderSection() {
  return render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <TenantHostsSection />
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('TenantHostsSection', () => {
  it('lists each host with its state, and the expiry of a pending one', async () => {
    const items: CellHostRequestState[] = [
      { host: 'a.example.com', state: 'pending', expiresAt },
      { host: 'b.example.com', state: 'verified', expiresAt },
      { host: 'c.example.com', state: 'expired', expiresAt },
    ]
    stubServer(json({ items }))
    renderSection()

    const section = await screen.findByRole('region', { name: 'Hosts' })
    expect(within(section).getByRole('heading', { name: 'Hosts', level: 2 })).toBeVisible()
    const rows = within(screen.getByTestId('tenant-hosts')).getAllByRole('row')
    expect(rows).toHaveLength(4)
    expect(within(rows[1]).getByText('a.example.com')).toBeVisible()
    expect(within(rows[1]).getByText('Pending')).toBeVisible()
    expect(within(rows[1]).getByText(expiry)).toBeVisible()
    expect(within(rows[2]).getByText('Verified')).toBeVisible()
    expect(within(rows[2]).queryByText(expiry)).toBeNull()
    expect(within(rows[3]).getByText('Expired')).toBeVisible()
    expect(within(rows[3]).queryByText(expiry)).toBeNull()
  })

  it('says that no host has been added when the list is empty', async () => {
    stubServer(json({ items: [] }))
    renderSection()

    expect(await screen.findByText(/No host has been added yet/)).toBeVisible()
  })

  it('adds a host, shows the TXT and CNAME records and the expiry, and loads the list again', async () => {
    let listed: CellHostRequestState[] = []
    const fetchMock = stubServer(() => json({ items: listed }))
    const user = userEvent.setup()
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Add a host…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add a host' })
    await user.type(within(dialog).getByRole('textbox', { name: 'Host' }), ' portal.example.com ')
    listed = [{ host: 'portal.example.com', state: 'pending', expiresAt }]
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    const records = await screen.findByTestId('tenant-hosts-records')
    const rows = within(records).getAllByRole('row')
    expect(rows).toHaveLength(3)
    expect(within(rows[1]).getByText('TXT')).toBeVisible()
    expect(within(rows[1]).getByText('_axis-challenge.portal.example.com')).toBeVisible()
    expect(within(rows[1]).getByText('axis-verify=abc')).toBeVisible()
    expect(within(rows[2]).getByText('CNAME')).toBeVisible()
    expect(within(rows[2]).getByText('cell.axis.example')).toBeVisible()
    expect(screen.getByText(`The request expires on ${expiry}.`)).toBeVisible()

    expect(posts(fetchMock)).toHaveLength(1)
    const [path, init] = posts(fetchMock)[0]
    expect(path).toBe('/api/tenant/hosts')
    expect(JSON.parse(init?.body as string)).toEqual({ host: 'portal.example.com' })
    await waitFor(() => expect(within(screen.getByTestId('tenant-hosts')).getByText('Pending')).toBeVisible())
  })

  it('sends nothing and names the missing host when the field is empty', async () => {
    const fetchMock = stubServer(json({ items: [] }))
    const user = userEvent.setup()
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Add a host…' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Add' }))

    expect(await screen.findByText('Enter a host.')).toBeInTheDocument()
    expect(posts(fetchMock)).toHaveLength(0)
  })

  it.each([
    [400, 'This is not a valid host name. Enter a domain name such as portal.example.com.'],
    [409, 'This host is already in use.'],
    [403, 'The host could not be added.'],
  ])('keeps the dialog open with a message in the person’s words when the add answers %i', async (status, message) => {
    stubServer(json({ items: [] }), json({ title: 'A server title in English only.' }, status))
    const user = userEvent.setup()
    renderSection()

    await user.click(await screen.findByRole('button', { name: 'Add a host…' }))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Host' }), 'portal.example.com')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    expect(await within(screen.getByTestId('tenant-hosts-error')).findByText(message)).toBeVisible()
    expect(screen.queryByText('A server title in English only.')).toBeNull()
    expect(screen.getByTestId('tenant-hosts-dialog')).toBeVisible()
  })

  it('renders nothing when the route answers 404', async () => {
    const fetchMock = stubServer(new Response(null, { status: 404 }))
    renderSection()

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/tenant/hosts', expect.anything()))
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)))

    expect(screen.queryByRole('region')).toBeNull()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('renders nothing when the route answers 403', async () => {
    const fetchMock = stubServer(new Response(null, { status: 403 }))
    renderSection()

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/tenant/hosts', expect.anything()))
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)))

    expect(screen.queryByRole('region')).toBeNull()
  })

  it('opens the dialog, fills the host, adds it and closes the dialog with the keyboard alone', async () => {
    const fetchMock = stubServer(json({ items: [] }))
    const user = userEvent.setup()
    renderSection()

    const addButton = await screen.findByRole('button', { name: 'Add a host…' })
    for (let presses = 0; presses < 10 && document.activeElement !== addButton; presses++) {
      await user.tab()
    }
    expect(addButton).toHaveFocus()
    await user.keyboard('{Enter}')

    const dialog = await screen.findByRole('dialog', { name: 'Add a host' })
    const field = within(dialog).getByRole('textbox', { name: 'Host' })
    for (let presses = 0; presses < 10 && document.activeElement !== field; presses++) {
      await user.tab()
    }
    expect(field).toHaveFocus()
    await user.keyboard('portal.example.com{Enter}')

    expect(await screen.findByTestId('tenant-hosts-records')).toBeVisible()
    expect(posts(fetchMock)).toHaveLength(1)

    // The header's cross is also named Close, so the footer button is the one with the text.
    const close = within(dialog)
      .getAllByRole('button', { name: 'Close' })
      .find((button) => button.textContent === 'Close')!
    for (let presses = 0; presses < 10 && document.activeElement !== close; presses++) {
      await user.tab()
    }
    expect(close).toHaveFocus()
    await user.keyboard('{Enter}')

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
  })
})
