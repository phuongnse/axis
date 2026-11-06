import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { CellJob, CellTenantDetail } from '../platform/cell'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { CellTenantPage } from './CellTenantPage'
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

const acme: CellTenantDetail = {
  id: 'acme',
  status: 'active',
  application: 'crm',
  hosts: ['acme.example.com', 'portal.acme.test'],
  failedWork: null,
  usage: {
    storage: { limit: null, used: null },
    users: { limit: null, used: null },
    applications: { limit: null, used: null },
    emailsPerDay: { limit: null, used: null },
  },
  newestArchiveAt: null,
  quotas: {
    users: { value: 100, overridden: true },
    applications: { value: 10, overridden: false },
    storage: { value: null, overridden: true },
  },
}
const job: CellJob = {
  id: '3',
  kind: 'suspend',
  tenantId: 'acme',
  state: 'succeeded',
  createdAt: '2026-11-05T10:00:00Z',
  steps: [{ name: 'suspend', state: 'succeeded', error: null }],
  error: null,
}

interface Answer {
  status: number
  body: unknown
}

const ok = (body: unknown): Answer => ({ status: 200, body })
const fail = (status: number): Answer => ({ status, body: { status } })

function stubRoutes(tenant: Answer, jobs: Answer = ok({ items: [job] })) {
  const fetchMock = vi.fn(async (input: string) => {
    const answer = input.startsWith('/api/cell/jobs') ? jobs : tenant
    return new Response(JSON.stringify(answer.body), { status: answer.status })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/**
 * Answers the actions of the page: each POST takes the next answer of its path and records its Idempotency-Key. The
 * tenant answers the status that the last POST that was answered with 202 asks for.
 */
function stubActions(posts: Record<string, Answer[]>, statuses: Record<string, CellTenantDetail['status']>) {
  const keys: (string | null)[] = []
  let status: CellTenantDetail['status'] = 'active'
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      keys.push(new Headers(init.headers).get('Idempotency-Key'))
      const answer = posts[input]!.shift()!
      if (answer.status === 202) {
        status = statuses[input]!
      }
      return new Response(JSON.stringify(answer.body), { status: answer.status })
    }
    if (input.startsWith('/api/cell/jobs/')) {
      return new Response(JSON.stringify({ ...job, id: input.split('/').pop() }), { status: 200 })
    }
    if (input.startsWith('/api/cell/jobs')) {
      return new Response(JSON.stringify({ items: [] }), { status: 200 })
    }
    return new Response(JSON.stringify({ ...acme, status }), { status: 200 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return keys
}

/**
 * Answers the quotas PUT and the hosts POST of the page with `answers`, by path, and records each write's method, path
 * and body. The tenant and its jobs answer as for a plain read.
 */
function stubWrites(answers: Record<string, Answer>) {
  const writes: { method: string; path: string; body: unknown }[] = []
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (init?.method === 'PUT' || init?.method === 'POST') {
      writes.push({ method: init.method, path: input, body: JSON.parse(init.body as string) })
      const answer = answers[input]!
      return new Response(JSON.stringify(answer.body), { status: answer.status })
    }
    if (input.startsWith('/api/cell/jobs')) {
      return new Response(JSON.stringify({ items: [] }), { status: 200 })
    }
    return new Response(JSON.stringify(acme), { status: 200 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return writes
}

const hostRequest = {
  host: 'portal.acme.test',
  token: 'secret-token',
  state: 'pending',
  expiresAt: '2026-11-12T10:00:00Z',
  txt: { name: '_axis-verify.portal.acme.test', value: 'axis-verify=secret-token' },
  cname: { name: 'portal.acme.test', target: 'acme.localhost' },
}

function renderPage(path: string, onRefused = vi.fn()) {
  render(
    <MemoryRouter initialEntries={[path]}>
      <ThemeModeProvider>
        <PlatformContext.Provider value={platform}>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <Routes>
              <Route element={<Outlet context={{ onRefused }} />}>
                <Route path="tenants/:id" element={<CellTenantPage />} />
              </Route>
            </Routes>
          </TextProvider>
        </PlatformContext.Provider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
  return onRefused
}

function cellTexts(row: HTMLElement) {
  return within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent)
}

describe('CellTenantPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the tenant, its quotas with their overrides and no limit, and its jobs', async () => {
    const fetchMock = stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    const details = await screen.findByTestId('cell-tenant')
    expect(details).toHaveTextContent('Active')
    expect(details).toHaveTextContent('crm')
    expect(details).toHaveTextContent('acme.example.com, portal.acme.test')

    const quotaRows = within(await screen.findByTestId('cell-tenant-quotas'))
      .getAllByRole('row')
      .slice(1)
    expect(quotaRows.map(cellTexts)).toEqual([
      ['Users', '100', 'Set for this tenant'],
      ['Applications', '10', 'Cell default'],
      ['Storage (bytes)', 'No limit', 'Set for this tenant'],
    ])

    const jobRows = within(await screen.findByTestId('cell-jobs')).getAllByRole('row')
    expect(jobRows).toHaveLength(2)
    expect(jobRows[1]).toHaveTextContent('suspend')
    expect(fetchMock).toHaveBeenCalledWith('/api/cell/jobs?tenant=acme', expect.anything())
  })

  it('shows the not-found page and no quotas for an unknown id', async () => {
    stubRoutes(fail(404))
    renderPage('/tenants/missing')

    expect(await screen.findByTestId('not-found')).toBeInTheDocument()
    expect(screen.queryByTestId('cell-tenant-quotas')).not.toBeInTheDocument()
  })

  it('reports a refusal of the tenant call to the console', async () => {
    stubRoutes(fail(401))
    const onRefused = renderPage('/tenants/acme')

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('unauthorized'))
  })

  it('shows an error when the tenant fails to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubRoutes(fail(500))
    renderPage('/tenants/acme')

    expect(await screen.findByTestId('cell-tenant-error')).toHaveTextContent('The tenant could not be loaded.')
    expect(screen.queryByTestId('cell-tenant')).not.toBeInTheDocument()
  })

  it('shows an error when the jobs fail to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubRoutes(ok(acme), fail(500))
    renderPage('/tenants/acme')

    expect(await screen.findByTestId('cell-tenant-jobs-error')).toHaveTextContent('The jobs could not be loaded.')
    expect(await screen.findByTestId('cell-tenant')).toBeInTheDocument()
  })

  it('offers Suspend and no Resume for an active tenant, and neither for a provisioning one', async () => {
    stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(screen.getByTestId('cell-tenant-suspend')).toBeInTheDocument()
    expect(screen.queryByTestId('cell-tenant-resume')).not.toBeInTheDocument()
  })

  it('offers both buttons for a read-only tenant and Resume alone for a suspended one', async () => {
    stubRoutes(ok({ ...acme, status: 'readOnly' }))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(screen.getByTestId('cell-tenant-suspend')).toBeInTheDocument()
    expect(screen.getByTestId('cell-tenant-resume')).toBeInTheDocument()
  })

  it('offers no action for a provisioning tenant', async () => {
    stubRoutes(ok({ ...acme, status: 'provisioning' }))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(screen.queryByTestId('cell-tenant-suspend')).not.toBeInTheDocument()
    expect(screen.queryByTestId('cell-tenant-resume')).not.toBeInTheDocument()
  })

  it('keeps the Idempotency-Key for a retry after a 5xx and sends a new one for the next action', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const queued = { id: '9', kind: 'suspend', tenantId: 'acme', state: 'queued', createdAt: job.createdAt }
    const keys = stubActions(
      {
        '/api/cell/tenants/acme/suspend': [fail(500), { status: 202, body: queued }],
        '/api/cell/tenants/acme/resume': [{ status: 202, body: { ...queued, id: '10', kind: 'resume' } }],
      },
      { '/api/cell/tenants/acme/suspend': 'suspended', '/api/cell/tenants/acme/resume': 'active' },
    )
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-suspend'))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Suspend acme?')
    await user.click(within(dialog).getByRole('button', { name: /Suspend$/ }))
    expect(await screen.findByTestId('cell-tenant-action-error')).toHaveTextContent('Try again.')
    await user.click(await within(dialog).findByRole('button', { name: /Suspend$/ }))

    await waitFor(() => expect(screen.getByTestId('cell-tenant')).toHaveTextContent('Suspended'))
    expect(screen.queryByTestId('cell-tenant-suspend')).not.toBeInTheDocument()
    await user.click(screen.getByTestId('cell-tenant-resume'))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: /Resume$/ }))

    await waitFor(() => expect(screen.getByTestId('cell-tenant')).toHaveTextContent('Active'))
    expect(keys).toHaveLength(3)
    expect(keys[0]).toBeTruthy()
    expect(keys[1]).toBe(keys[0])
    expect(keys[2]).toBeTruthy()
    expect(keys[2]).not.toBe(keys[0])
  })

  it('reports a refusal of an action to the console', async () => {
    const keys = stubActions({ '/api/cell/tenants/acme/suspend': [fail(403)] }, {})
    const user = userEvent.setup()
    const onRefused = renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-suspend'))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: /Suspend$/ }))

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('forbidden'))
    expect(keys).toHaveLength(1)
  })

  it('sends only the changed quota keys and shows the answered quotas', async () => {
    const answered = {
      users: { value: 100, overridden: false },
      applications: { value: 25, overridden: true },
      storage: { value: null, overridden: true },
    }
    const writes = stubWrites({ '/api/cell/tenants/acme/quotas': ok({ quotas: answered }) })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-set-quotas'))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Set the quotas of acme')
    // Users is overridden, Applications is the default and Storage has no limit. Storage stays as it is.
    await user.click(within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByLabelText('Cell default'))
    await user.click(within(within(dialog).getByRole('radiogroup', { name: 'Applications' })).getByLabelText('A value'))
    const applications = within(dialog).getByRole('spinbutton', { name: 'Applications value' })
    await user.clear(applications)
    await user.type(applications, '25')
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(writes).toEqual([
      { method: 'PUT', path: '/api/cell/tenants/acme/quotas', body: { set: { applications: 25 }, remove: ['users'] } },
    ])
    const quotaRows = within(screen.getByTestId('cell-tenant-quotas')).getAllByRole('row').slice(1)
    expect(quotaRows.map(cellTexts)).toEqual([
      ['Users', '100', 'Cell default'],
      ['Applications', '25', 'Set for this tenant'],
      ['Storage (bytes)', 'No limit', 'Set for this tenant'],
    ])
  })

  it('sends no limit as null', async () => {
    const writes = stubWrites({ '/api/cell/tenants/acme/quotas': ok({ quotas: acme.quotas }) })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-set-quotas'))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByLabelText('No limit'))
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(writes).toHaveLength(1))
    expect(writes[0]!.body).toEqual({ set: { users: null }, remove: [] })
  })

  it('keeps Save off until a quota changes', async () => {
    stubWrites({})
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-set-quotas'))
    const dialog = await screen.findByRole('dialog')
    const save = within(dialog).getByRole('button', { name: 'Save' })
    expect(save).toBeDisabled()
    await user.click(within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByLabelText('No limit'))
    expect(save).toBeEnabled()
    await user.click(within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByLabelText('A value'))
    expect(save).toBeDisabled()
  })

  it('offers Set quotas and Add a host for every status', async () => {
    stubRoutes(ok({ ...acme, status: 'provisioning' }))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(screen.getByTestId('cell-tenant-set-quotas')).toBeInTheDocument()
    expect(screen.getByTestId('cell-tenant-add-host')).toBeInTheDocument()
  })

  it('adds a host and shows its TXT and CNAME records', async () => {
    const writes = stubWrites({ '/api/cell/tenants/acme/hosts': { status: 201, body: hostRequest } })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-add-host'))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Add a host to acme')
    const add = within(dialog).getByRole('button', { name: 'Add' })
    expect(add).toBeDisabled()
    await user.type(within(dialog).getByLabelText('Host'), 'portal.acme.test')
    await user.click(add)

    const records = await within(dialog).findByTestId('cell-tenant-host-records')
    const rows = within(records).getAllByRole('row').slice(1)
    expect(rows.map(cellTexts)).toEqual([
      ['TXT', '_axis-verify.portal.acme.test', 'axis-verify=secret-token'],
      ['CNAME', 'portal.acme.test', 'acme.localhost'],
    ])
    expect(dialog).toHaveTextContent('The request expires on')
    expect(writes).toEqual([
      { method: 'POST', path: '/api/cell/tenants/acme/hosts', body: { host: 'portal.acme.test' } },
    ])
    // The dialog's corner icon is also named Close, and the footer button comes last.
    await user.click(within(dialog).getAllByRole('button', { name: 'Close' }).at(-1)!)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('keeps the host dialog open with the title of a refusal', async () => {
    stubWrites({
      '/api/cell/tenants/acme/hosts': { status: 409, body: { title: 'Another tenant holds this host.' } },
    })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-add-host'))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Host'), 'taken.example.com')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    expect(await screen.findByTestId('cell-tenant-host-error')).toHaveTextContent(
      'The cell refused the request: Another tenant holds this host.',
    )
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.queryByTestId('cell-tenant-host-records')).not.toBeInTheDocument()
  })

  it('reports a forbidden host request to the console', async () => {
    stubWrites({ '/api/cell/tenants/acme/hosts': { status: 403, body: { status: 403 } } })
    const user = userEvent.setup()
    const onRefused = renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-add-host'))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Host'), 'portal.acme.test')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('forbidden'))
  })

  it('shows the did-not-reach message when the quotas request fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubWrites({ '/api/cell/tenants/acme/quotas': fail(500) })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-set-quotas'))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByLabelText('No limit'))
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await screen.findByTestId('cell-tenant-quotas-error')).toHaveTextContent(
      'The request did not reach the cell.',
    )
  })
})
