import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { MemoryRouter, Outlet, Route, Routes, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import platformVietnamese from '../../../src/Axis.Presentation/Texts/vi.json'
import {
  cellTenantStatusTones,
  type CellJob,
  type CellTenantDetail,
  type CellTenantUsageDayPage,
} from '../platform/cell'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { CellTenantPage } from './CellTenantPage'
import { PlatformContext, type Platform } from './context'
import { hasFieldError, isRequiredLabel, shownMenu } from '../test/antd'
import { clickInDialog, pick } from '../test/tableFilter'

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
  deleteAfter: null,
  quotas: {
    users: { value: 100, overridden: true },
    applications: { value: 10, overridden: false },
    storage: { value: null, overridden: true },
  },
  days: [],
  hostRequests: [],
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

const noDays: CellTenantUsageDayPage = { items: [], page: 1, pageSize: 20, totalCount: 0 }

/** The answer to a read of the usage days route, which the stubs give before the tenant. */
const usageDays = (input: string, page: CellTenantUsageDayPage = noDays) =>
  input.startsWith('/api/cell/tenants/acme/usage-days')
    ? new Response(JSON.stringify(page), { status: 200 })
    : undefined

function stubRoutes(
  tenant: Answer,
  jobs: Answer = ok({ items: [job] }),
  deleted: Answer = fail(404),
  days: CellTenantUsageDayPage = noDays,
) {
  const fetchMock = vi.fn(async (input: string) => {
    const daysAnswer = usageDays(input, days)
    if (daysAnswer) {
      return daysAnswer
    }
    const answer = input.startsWith('/api/cell/jobs')
      ? jobs
      : input.startsWith('/api/cell/deleted-tenants/')
        ? deleted
        : tenant
    return new Response(JSON.stringify(answer.body), { status: answer.status })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/**
 * Answers the actions of the page: each POST or DELETE takes the next answer of its method and path, such as
 * `POST /api/cell/tenants/acme/suspend`, and records its Idempotency-Key. The tenant answers the status that the last
 * action that was answered with 202 asks for, and starts as `initial`.
 */
function stubActions(
  requests: Record<string, Answer[]>,
  statuses: Record<string, CellTenantDetail['status']>,
  initial: CellTenantDetail['status'] = 'active',
) {
  const keys: (string | null)[] = []
  const sent: string[] = []
  let status = initial
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (init?.method === 'POST' || init?.method === 'DELETE') {
      const request = `${init.method} ${input}`
      sent.push(request)
      keys.push(new Headers(init.headers).get('Idempotency-Key'))
      const answer = requests[request]!.shift()!
      if (answer.status === 202) {
        status = statuses[request]!
      }
      return new Response(JSON.stringify(answer.body), { status: answer.status })
    }
    if (usageDays(input)) {
      return usageDays(input)!
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
  return { keys, sent }
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
    if (usageDays(input)) {
      return usageDays(input)!
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

const exportJob: CellJob = {
  id: '7f3c1a52-0000-4000-8000-000000000001',
  kind: 'export',
  tenantId: 'acme',
  state: 'succeeded',
  createdAt: '2026-11-05T11:00:00Z',
  steps: [],
  error: null,
}

/**
 * Answers the tenant, its jobs and the export download. Each download takes the next answer of `downloads` and records
 * its Accept header.
 */
function stubDownload(jobs: CellJob[], downloads: Response[] = []) {
  const accepts: (string | null)[] = []
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input.startsWith('/api/cell/tenants/acme/exports/')) {
      accepts.push(new Headers(init?.headers).get('Accept'))
      return downloads.shift()!
    }
    if (usageDays(input)) {
      return usageDays(input)!
    }
    if (input.startsWith('/api/cell/jobs')) {
      return new Response(JSON.stringify({ items: jobs }), { status: 200 })
    }
    return new Response(JSON.stringify(acme), { status: 200 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { accepts, fetchMock }
}

function Address() {
  return <output data-testid="address">{useLocation().search}</output>
}

function renderPage(path: string, onRefused = vi.fn(), vietnamese = false) {
  const current: Platform = vietnamese
    ? { ...platform, current: { locale: 'vi', texts: platformVietnamese } }
    : platform
  render(
    <MemoryRouter initialEntries={[path]}>
      <ThemeModeProvider>
        <PlatformContext.Provider value={current}>
          <TextProvider
            catalogs={[{ texts: vietnamese ? platformVietnamese : platformEnglish, fallbackTexts: platformEnglish }]}
            development={false}
          >
            <Routes>
              <Route element={<Outlet context={{ onRefused }} />}>
                <Route path="tenants/:id" element={<CellTenantPage />} />
              </Route>
            </Routes>
          </TextProvider>
        </PlatformContext.Provider>
      </ThemeModeProvider>
      <Address />
    </MemoryRouter>,
  )
  return onRefused
}

function cellTexts(row: HTMLElement) {
  return within(row)
    .getAllByRole('cell')
    .map((cell) => cell.textContent)
}

/** The tag of the tenant's status, on the page's heading. */
function statusTag(text: string) {
  return within(screen.getByTestId('page-heading')).getByText(text).closest('[data-tone]')
}

/**
 * Opens the More actions menu of the page's heading, as a person does, and returns the menu once it has the focus. The
 * menu takes the focus a few frames after it opens, so a choice made before then would lose it to the menu again.
 */
async function openMore(user: UserEvent) {
  await user.click(within(await screen.findByTestId('page-actions')).getByRole('button', { name: 'More actions' }))
  const menu = await shownMenu('More actions')
  await waitFor(() => expect(menu.contains(document.activeElement)).toBe(true))
  return within(menu)
}

/** Chooses the entry `testId` in the More actions menu of the heading, for an action that folds into it. */
async function chooseMore(user: UserEvent, testId: string) {
  await user.click((await openMore(user)).getByTestId(testId))
}

describe('CellTenantPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('shows the tenant, its quotas with their overrides and no limit, and its jobs', async () => {
    const fetchMock = stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    const details = await screen.findByTestId('cell-tenant')
    expect(details).not.toHaveTextContent('Status')
    expect(details).toHaveTextContent('crm')
    expect(details).toHaveTextContent('acme.example.com and portal.acme.test')

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
    // The tenant's own jobs name the tenant of the page, so it is not a link back to it.
    expect(within(jobRows[1]).queryByRole('link')).toBeNull()
    expect(fetchMock).toHaveBeenCalledWith('/api/cell/jobs?tenant=acme&pageSize=50', expect.anything())
  })

  it('joins the hosts in the list words of the Vietnamese locale', async () => {
    stubRoutes(ok(acme))
    renderPage('/tenants/acme', vi.fn(), true)

    expect(await screen.findByTestId('cell-tenant')).toHaveTextContent('acme.example.com và portal.acme.test')
  })

  it('lists each requested host as pending with its expiry, verified or expired', async () => {
    stubRoutes(
      ok({
        ...acme,
        hostRequests: [
          { host: 'old.acme.test', state: 'expired', expiresAt: '2026-11-01T10:00:00Z' },
          { host: 'portal.acme.test', state: 'pending', expiresAt: '2026-11-12T10:00:00Z' },
          { host: 'shop.acme.test', state: 'verified', expiresAt: '2026-11-05T10:00:00Z' },
        ],
      }),
    )
    renderPage('/tenants/acme')

    const rows = within(await screen.findByTestId('cell-tenant-host-requests'))
      .getAllByRole('row')
      .slice(1)
    expect(rows.map(cellTexts)).toEqual([
      ['old.acme.test', 'Expired', '—'],
      ['portal.acme.test', 'Pending', new Date('2026-11-12T10:00:00Z').toLocaleString('en')],
      ['shop.acme.test', 'Verified', '—'],
    ])
  })

  it('filters the host verification on its state, kept in its own address name', async () => {
    stubRoutes(
      ok({
        ...acme,
        hostRequests: [
          { host: 'old.acme.test', state: 'expired', expiresAt: '2026-11-01T10:00:00Z' },
          { host: 'portal.acme.test', state: 'pending', expiresAt: '2026-11-12T10:00:00Z' },
          { host: 'shop.acme.test', state: 'verified', expiresAt: '2026-11-05T10:00:00Z' },
        ],
      }),
    )
    renderPage('/tenants/acme')

    const hosts = await screen.findByTestId('cell-tenant-host-requests')
    await userEvent.click(within(hosts).getByRole('button', { name: 'Filter' }))
    await pick('Column', 0, 'State')
    await pick('Value', 0, 'Pending')
    await clickInDialog('Apply')

    const filter = { all: [{ field: 'state', op: 'eq', value: 'pending' }] }
    await waitFor(() =>
      expect(within(hosts).getAllByRole('row').slice(1).map(cellTexts)).toEqual([
        ['portal.acme.test', 'Pending', new Date('2026-11-12T10:00:00Z').toLocaleString('en')],
      ]),
    )
    expect(
      JSON.parse(new URLSearchParams(screen.getByTestId('address').textContent!).get('hostRequests.filter')!),
    ).toEqual(filter)
  })

  it('filters the quotas on their source, kept in its own address name and never sent to the tenant read', async () => {
    const fetchMock = stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    const quotas = await screen.findByTestId('cell-tenant-quotas')
    await userEvent.click(within(quotas).getByRole('button', { name: 'Filter' }))
    await pick('Column', 0, 'Source')
    await pick('Value', 0, 'Cell default')
    await clickInDialog('Apply')

    await waitFor(() =>
      expect(within(quotas).getAllByRole('row').slice(1).map(cellTexts)).toEqual([
        ['Applications', '10', 'Cell default'],
      ]),
    )
    const filter = { all: [{ field: 'source', op: 'eq', value: 'default' }] }
    expect(JSON.parse(new URLSearchParams(screen.getByTestId('address').textContent!).get('quotas.filter')!)).toEqual(
      filter,
    )
    const tenantUrls = fetchMock.mock.calls
      .map(([input]) => String(input))
      .filter((url) => url.startsWith('/api/cell/tenants/acme'))
    expect(tenantUrls.length).toBeGreaterThan(0)
    expect(tenantUrls.some((url) => new URL(url, 'http://localhost').searchParams.has('filter'))).toBe(false)
  })

  it('filters the host verification on the day its request expires, and no other state has one', async () => {
    stubRoutes(
      ok({
        ...acme,
        hostRequests: [
          { host: 'portal.acme.test', state: 'pending', expiresAt: '2026-11-12T10:00:00Z' },
          { host: 'shop.acme.test', state: 'pending', expiresAt: '2026-11-20T10:00:00Z' },
          { host: 'old.acme.test', state: 'verified', expiresAt: '2026-11-12T10:00:00Z' },
        ],
      }),
    )
    renderPage('/tenants/acme')

    const hosts = await screen.findByTestId('cell-tenant-host-requests')
    await userEvent.click(within(hosts).getByRole('button', { name: 'Filter' }))
    await pick('Column', 0, 'Expires')
    await userEvent.type(
      within(screen.getByRole('dialog')).getByRole('textbox', { name: 'Value' }),
      '2026-11-12{Enter}',
    )
    await clickInDialog('Apply')

    await waitFor(() => expect(within(hosts).getAllByRole('row').slice(1).map(cellTexts)).toHaveLength(1))
    expect(within(hosts).getAllByRole('row')[1]).toHaveTextContent('portal.acme.test')
  })

  it('filters the quotas on their value, and a quota with no limit is empty', async () => {
    stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    const quotas = await screen.findByTestId('cell-tenant-quotas')
    await userEvent.click(within(quotas).getByRole('button', { name: 'Filter' }))
    await pick('Column', 0, 'Value')
    await pick('Operator', 0, 'is more than')
    await userEvent.type(within(screen.getByRole('dialog')).getByRole('spinbutton', { name: 'Value' }), '50')
    await clickInDialog('Apply')

    await waitFor(() =>
      expect(within(quotas).getAllByRole('row').slice(1).map(cellTexts)).toEqual([
        ['Users', '100', 'Set for this tenant'],
      ]),
    )
  })

  it('says that no host has been requested', async () => {
    stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    expect(await screen.findByTestId('cell-tenant-host-requests')).toHaveTextContent(
      'No host has been requested. Add a host to start its verification.',
    )
  })

  it('lists the days of the usage days route in the usage table', async () => {
    const day = {
      day: '2026-11-04',
      databaseBytes: 2048,
      fileBytes: 0,
      users: 3,
      applications: null,
      emailsSent: 7,
      connectorCalls: 9,
      workItemsRun: 4321,
    }
    stubRoutes(ok(acme), undefined, undefined, { ...noDays, items: [day], totalCount: 1 })
    renderPage('/tenants/acme')

    const table = await screen.findByTestId('cell-tenant-usage')
    expect(await within(table).findByText('4,321')).toBeVisible()
    const rows = within(table).getAllByRole('row')
    expect(rows).toHaveLength(2)
    expect(rows[1]).toHaveTextContent('4,321')
  })

  it('shows the empty text when the usage days route has no days', async () => {
    stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    expect(await screen.findByTestId('cell-tenant-usage')).toHaveTextContent('No usage recorded yet.')
  })

  it('keeps the filter, sort and page size of the usage table in the address and the request', async () => {
    const filter = JSON.stringify({ all: [{ field: 'workItemsRun', op: 'gte', value: 300 }] })
    const fetchMock = stubRoutes(ok(acme), undefined, undefined, { ...noDays, totalCount: 45 })
    const user = userEvent.setup()
    renderPage(`/tenants/acme?${new URLSearchParams({ filter, sort: '-day' })}`)

    const table = await screen.findByTestId('cell-tenant-usage')
    expect(await within(table).findByText('1–20 of 45')).toBeVisible()
    const requests = () =>
      fetchMock.mock.calls
        .map(([url]) => String(url))
        .filter((url) => url.startsWith('/api/cell/tenants/acme/usage-days'))
    const first = new URL(requests()[0]!, 'http://localhost').searchParams
    expect(first.get('filter')).toBe(filter)
    expect(first.get('sort')).toBe('-day')

    await user.click(within(table).getByRole('combobox'))
    await user.click(await screen.findByTitle('50 / page'))

    await waitFor(() => expect(screen.getByTestId('address').textContent).toContain('pageSize=50'))
    await waitFor(() =>
      expect(requests().some((url) => new URL(url, 'http://localhost').searchParams.get('pageSize') === '50')).toBe(
        true,
      ),
    )
  })

  it('shows an error in the usage part only when the days fail to load', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) =>
        input.startsWith('/api/cell/tenants/acme/usage-days')
          ? new Response('{}', { status: 500 })
          : input.startsWith('/api/cell/jobs')
            ? new Response(JSON.stringify({ items: [] }), { status: 200 })
            : new Response(JSON.stringify(acme), { status: 200 }),
      ),
    )
    renderPage('/tenants/acme')

    expect(await screen.findByTestId('cell-tenant-usage-error')).toHaveTextContent('The usage could not be loaded.')
    expect(screen.getByTestId('cell-tenant')).toHaveTextContent('crm')
    warn.mockRestore()
  })

  it('shows the not-found page and no quotas for an unknown id', async () => {
    stubRoutes(fail(404))
    renderPage('/tenants/missing')

    expect(await screen.findByTestId('not-found')).toBeInTheDocument()
    expect(screen.queryByTestId('cell-tenant-quotas')).not.toBeInTheDocument()
  })

  it('heads the page with the tenant id, its status as a tag in the tone of the tenants list and its kind as the line', async () => {
    stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    const heading = within(screen.getByTestId('page-heading'))
    expect(heading.getByRole('heading', { level: 1, name: 'acme' })).toBeInTheDocument()
    expect(heading.getByText('Tenant')).toBeInTheDocument()
    expect(statusTag('Active')).toHaveAttribute('data-tone', cellTenantStatusTones.active)
    expect(statusTag('Active')).toHaveAttribute('data-tone', 'neutral')
  })

  it.each([
    ['suspended', 'Suspended', 'attention'],
    ['readOnly', 'Read only', 'attention'],
    ['provisioning', 'Provisioning', 'neutral'],
  ] as const)('tags a %s tenant in the tone of the tenants list', async (status, words, tone) => {
    stubRoutes(ok({ ...acme, status }))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(statusTag(words)).toHaveAttribute('data-tone', tone)
  })

  it('puts Export on the heading and folds the other actions into More actions, Suspend and Delete last', async () => {
    stubRoutes(ok(acme))
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    const actions = within(await screen.findByTestId('page-actions'))
    expect(actions.getAllByRole('button')).toHaveLength(2)
    expect(actions.getByTestId('cell-tenant-export')).toBeInTheDocument()
    const menu = await openMore(user)
    expect(menu.getAllByRole('menuitem').map((entry) => entry.textContent)).toEqual([
      'Set quotas…',
      'Add a host…',
      'Suspend…',
      'Delete…',
    ])
    expect(menu.getAllByRole('separator')).toHaveLength(1)
    expect(menu.getByTestId('cell-tenant-suspend')).toBeInTheDocument()
  })

  it('opens Suspend from the More actions menu, and its confirmation names the tenant', async () => {
    stubRoutes(ok(acme))
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-suspend')
    expect(await screen.findByRole('dialog')).toHaveTextContent('Suspend acme?')
  })

  it('shows the purge date of a deleting tenant next to its status', async () => {
    stubRoutes(ok({ ...acme, status: 'deleting', deleteAfter: '2026-11-12T10:00:00Z' }))
    renderPage('/tenants/acme')

    const details = await screen.findByTestId('cell-tenant')
    expect(statusTag('Deleting')).toHaveAttribute('data-tone', 'neutral')
    expect(details).toHaveTextContent(`Purge after${new Date('2026-11-12T10:00:00Z').toLocaleString('en')}`)
  })

  it('shows no purge date for a tenant that is not deleting', async () => {
    stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    expect(await screen.findByTestId('cell-tenant')).not.toHaveTextContent('Purge after')
  })

  it('shows a purged id as Deleted with both dates, its jobs and no action', async () => {
    const fetchMock = stubRoutes(
      fail(404),
      ok({ items: [{ ...job, kind: 'purge', tenantId: 'gone' }] }),
      ok({ id: 'gone', deletedAt: '2026-11-01T00:00:00Z', completeAt: '2026-11-08T00:00:00Z' }),
    )
    renderPage('/tenants/gone')

    const details = await screen.findByTestId('cell-tenant-deleted')
    expect(statusTag('Deleted')).toHaveAttribute('data-tone', 'muted')
    expect(within(screen.getByTestId('page-actions')).queryByRole('button')).not.toBeInTheDocument()
    expect(details).toHaveTextContent(`Purged${new Date('2026-11-01T00:00:00Z').toLocaleString('en')}`)
    expect(details).toHaveTextContent(`Deletion complete${new Date('2026-11-08T00:00:00Z').toLocaleString('en')}`)
    expect(await screen.findByText('purge')).toBeInTheDocument()
    expect(fetchMock).toHaveBeenCalledWith('/api/cell/deleted-tenants/gone', expect.anything())
    for (const action of [
      'suspend',
      'resume',
      'export',
      'delete',
      'cancel-deletion',
      'download',
      'set-quotas',
      'add-host',
    ]) {
      expect(screen.queryByTestId(`cell-tenant-${action}`)).not.toBeInTheDocument()
    }
    expect(screen.queryByTestId('cell-tenant-quotas')).not.toBeInTheDocument()
    expect(screen.queryByTestId('not-found')).not.toBeInTheDocument()
  })

  it('shows the not-found page for an id that is neither a tenant nor a purged tenant', async () => {
    stubRoutes(fail(404), ok({ items: [] }), fail(404))
    renderPage('/tenants/never')

    expect(await screen.findByTestId('not-found')).toBeInTheDocument()
    expect(screen.queryByTestId('cell-tenant-deleted')).not.toBeInTheDocument()
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

    const error = await screen.findByTestId('cell-tenant-jobs-error')
    expect(error).toHaveTextContent('The jobs could not be loaded.')
    expect(screen.getByTestId('studio-section-cell-tenant-jobs')).toContainElement(error)
    expect(screen.getByRole('heading', { name: 'Jobs' }).compareDocumentPosition(error)).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    )
    expect(await screen.findByTestId('cell-tenant')).toBeInTheDocument()
  })

  it('shows the facts, then Host verification, Quotas, Usage and Jobs as parts with no card', async () => {
    stubRoutes(ok(acme))
    renderPage('/tenants/acme')

    for (const name of ['Host verification', 'Quotas', 'Usage', 'Jobs']) {
      const region = await screen.findByRole('region', { name })
      expect(within(region).getByRole('heading', { level: 2, name })).toBeInTheDocument()
    }
    expect(screen.getAllByRole('separator')).toHaveLength(4)
    expect(document.querySelector('.ant-card')).toBeNull()
  })

  it('shows only the Jobs part, with no rule, when the tenant fails to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubRoutes(fail(500))
    renderPage('/tenants/acme')

    expect(await screen.findByTestId('cell-tenant-error')).toBeInTheDocument()
    const jobs = await screen.findByTestId('cell-jobs')
    const regions = screen.getAllByRole('region')
    expect(regions).toHaveLength(1)
    expect(regions[0]).toContainElement(jobs)
    expect(within(regions[0]).getByRole('heading', { level: 2, name: 'Jobs' })).toBeInTheDocument()
    expect(screen.queryByRole('separator')).not.toBeInTheDocument()
  })

  it('offers Suspend in the More actions menu and no Resume for an active tenant', async () => {
    stubRoutes(ok(acme))
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    const menu = await openMore(user)
    expect(menu.getByTestId('cell-tenant-suspend')).toBeInTheDocument()
    expect(menu.queryByTestId('cell-tenant-resume')).not.toBeInTheDocument()
  })

  it('offers Resume on the heading and Suspend in the More actions menu for a read-only tenant', async () => {
    stubRoutes(ok({ ...acme, status: 'readOnly' }))
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(within(screen.getByTestId('page-actions')).getByTestId('cell-tenant-resume')).toBeInTheDocument()
    expect((await openMore(user)).getByTestId('cell-tenant-suspend')).toBeInTheDocument()
  })

  it('offers no Suspend or Resume for a provisioning tenant, and no More actions button', async () => {
    stubRoutes(ok({ ...acme, status: 'provisioning' }))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(screen.queryByTestId('cell-tenant-suspend')).not.toBeInTheDocument()
    expect(screen.queryByTestId('cell-tenant-resume')).not.toBeInTheDocument()
    expect(
      within(screen.getByTestId('page-actions')).queryByRole('button', { name: 'More actions' }),
    ).not.toBeInTheDocument()
  })

  it('keeps the Idempotency-Key for a retry after a 5xx and sends a new one for the next action', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const queued = { id: '9', kind: 'suspend', tenantId: 'acme', state: 'queued', createdAt: job.createdAt }
    const { keys } = stubActions(
      {
        'POST /api/cell/tenants/acme/suspend': [fail(500), { status: 202, body: queued }],
        'POST /api/cell/tenants/acme/resume': [{ status: 202, body: { ...queued, id: '10', kind: 'resume' } }],
      },
      { 'POST /api/cell/tenants/acme/suspend': 'suspended', 'POST /api/cell/tenants/acme/resume': 'active' },
    )
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-suspend')
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Suspend acme?')
    await user.click(within(dialog).getByRole('button', { name: /Suspend$/ }))
    const alert = await screen.findByTestId('cell-tenant-action-error')
    expect(alert).toHaveTextContent('Try again.')
    expect(
      alert.compareDocumentPosition(within(dialog).getByText(/^The hosts of the tenant answer 503/)) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
    await user.click(await within(dialog).findByRole('button', { name: /Suspend$/ }))

    await waitFor(() => expect(statusTag('Suspended')).toBeInTheDocument())
    expect((await openMore(user)).queryByTestId('cell-tenant-suspend')).not.toBeInTheDocument()
    await user.keyboard('{Escape}')
    await user.click(screen.getByTestId('cell-tenant-resume'))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: /Resume$/ }))

    await waitFor(() => expect(statusTag('Active')).toBeInTheDocument())
    expect(keys).toHaveLength(3)
    expect(keys[0]).toBeTruthy()
    expect(keys[1]).toBe(keys[0])
    expect(keys[2]).toBeTruthy()
    expect(keys[2]).not.toBe(keys[0])
  })

  it('reports a refusal of an action to the console', async () => {
    const { keys } = stubActions({ 'POST /api/cell/tenants/acme/suspend': [fail(403)] }, {})
    const user = userEvent.setup()
    const onRefused = renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-suspend')
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

    await chooseMore(user, 'cell-tenant-set-quotas')
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Set the quotas of acme')
    // Users is overridden, Applications is the default and Storage has no limit. Storage stays as it is.
    await user.click(
      within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByRole('radio', { name: /^Cell default/ }),
    )
    await user.click(
      within(within(dialog).getByRole('radiogroup', { name: 'Applications' })).getByRole('radio', { name: /^A value/ }),
    )
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

    await chooseMore(user, 'cell-tenant-set-quotas')
    const dialog = await screen.findByRole('dialog')
    await user.click(
      within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByRole('radio', { name: /^No limit/ }),
    )
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(writes).toHaveLength(1))
    expect(writes[0]!.body).toEqual({ set: { users: null }, remove: [] })
  })

  it('keeps Save off until a quota changes', async () => {
    stubWrites({})
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-set-quotas')
    const dialog = await screen.findByRole('dialog')
    const save = within(dialog).getByRole('button', { name: 'Save' })
    expect(save).toBeDisabled()
    await user.click(
      within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByRole('radio', { name: /^No limit/ }),
    )
    expect(save).toBeEnabled()
    await user.click(
      within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByRole('radio', { name: /^A value/ }),
    )
    expect(save).toBeDisabled()
  })

  it('draws each quota choice with a required label, its help and the help of each option', async () => {
    stubWrites({})
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-set-quotas')
    const dialog = await screen.findByRole('dialog')

    for (const label of ['Users', 'Applications', 'Storage (bytes)']) {
      const group = within(dialog).getByRole('radiogroup', { name: label })
      expect(isRequiredLabel(within(dialog).getByText(label))).toBe(true)
      expect(group).toHaveAccessibleDescription('How this quota is set for this tenant.')
      const helps = {
        'A value': 'Use the number in the value field below.',
        'No limit': 'This tenant has no limit for this quota.',
        'Cell default': "Use the cell's default, and follow it when it changes.",
      }
      for (const [option, help] of Object.entries(helps)) {
        const radio = within(group).getByRole('radio', { name: new RegExp(`^${option}`) })
        expect(radio).toHaveAccessibleDescription(help)
      }
    }
  })

  it('keeps Save on with an empty value and checks it on click', async () => {
    const writes = stubWrites({})
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-set-quotas')
    const dialog = await screen.findByRole('dialog')
    const users = within(dialog).getByRole('spinbutton', { name: 'Users value' })
    await user.clear(users)
    const save = within(dialog).getByRole('button', { name: 'Save' })
    expect(save).toBeEnabled()
    await user.click(save)

    const message = await within(dialog).findByText('Enter a value.')
    expect(users.closest('.ant-form-item')).toContainElement(message)
    expect(users.compareDocumentPosition(message) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(hasFieldError(users)).toBe(true)
    await waitFor(() => expect(users).toHaveFocus())
    expect(writes).toHaveLength(0)
  })

  it('offers Export for every status but provisioning, in the More actions menu of a suspended tenant', async () => {
    stubRoutes(ok({ ...acme, status: 'suspended' }))
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    expect((await openMore(user)).getByTestId('cell-tenant-export')).toBeInTheDocument()
  })

  it('queues an export job with an Idempotency-Key after the confirmation names the tenant', async () => {
    const queued = { id: '11', kind: 'export', tenantId: 'acme', state: 'queued', createdAt: job.createdAt }
    const { keys, sent } = stubActions(
      { 'POST /api/cell/tenants/acme/exports': [{ status: 202, body: queued }] },
      { 'POST /api/cell/tenants/acme/exports': 'active' },
    )
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-export'))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Export acme?')
    await user.click(within(dialog).getByRole('button', { name: /Export$/ }))

    await waitFor(() => expect(keys).toHaveLength(1))
    expect(sent).toEqual(['POST /api/cell/tenants/acme/exports'])
    expect(keys[0]).toBeTruthy()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('offers Download only with a succeeded export job', async () => {
    stubDownload([{ ...exportJob, state: 'failed' }, job])
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    await screen.findByTestId('cell-jobs')
    const menu = await openMore(user)
    expect(menu.getByTestId('cell-tenant-set-quotas')).toBeInTheDocument()
    expect(menu.queryByTestId('cell-tenant-download')).not.toBeInTheDocument()
  })

  it('sends the ZIP request only after the warning is accepted, and saves the file under the name of the server', async () => {
    const createObjectURL = vi.fn((_blob: Blob) => 'blob:export')
    const revokeObjectURL = vi.fn()
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL }))
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    const { accepts, fetchMock } = stubDownload(
      [exportJob, job],
      [
        new Response('zip', {
          status: 200,
          headers: { 'Content-Disposition': `attachment; filename="acme-${exportJob.id}.zip"` },
        }),
      ],
    )
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-download')
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Download the export of acme?')
    expect(dialog).toHaveTextContent('restricted data')
    expect(accepts).toEqual([])

    await user.click(within(dialog).getByRole('button', { name: /Download$/ }))

    await waitFor(() => expect(click).toHaveBeenCalledTimes(1))
    expect(accepts).toEqual(['application/zip'])
    expect(fetchMock).toHaveBeenCalledWith(`/api/cell/tenants/acme/exports/${exportJob.id}`, expect.anything())
    expect((click.mock.contexts[0] as HTMLAnchorElement).download).toBe(`acme-${exportJob.id}.zip`)
    expect(createObjectURL).toHaveBeenCalledTimes(1)
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    click.mockRestore()
  })

  it('sends no ZIP request when the warning is cancelled', async () => {
    const { accepts } = stubDownload([exportJob, job])
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-download')
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(accepts).toEqual([])
  })

  it('keeps the warning open with the title of a 410 answer', async () => {
    const createObjectURL = vi.fn((_blob: Blob) => 'blob:export')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL, revokeObjectURL: vi.fn() }))
    stubDownload([exportJob], [new Response(JSON.stringify({ title: 'The export has expired.' }), { status: 410 })])
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-download')
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: /Download$/ }))

    const alert = await screen.findByTestId('cell-tenant-download-error')
    expect(alert).toHaveTextContent('The export has expired.')
    expect(
      alert.compareDocumentPosition(within(dialog).getByText(/^The ZIP is restricted data/)) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('reports a refused download to the console', async () => {
    stubDownload([exportJob], [new Response('{}', { status: 403 })])
    const user = userEvent.setup()
    const onRefused = renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-download')
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: /Download$/ }))

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('forbidden'))
  })

  it('offers Resume on the heading and Delete last in the More actions menu for a suspended tenant', async () => {
    stubRoutes(ok({ ...acme, status: 'suspended' }))
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(within(screen.getByTestId('page-actions')).getByTestId('cell-tenant-resume')).toBeInTheDocument()
    const menu = await openMore(user)
    expect(menu.getByTestId('cell-tenant-delete')).toBeInTheDocument()
    expect(menu.queryByTestId('cell-tenant-cancel-deletion')).not.toBeInTheDocument()
  })

  it('offers no Delete for a provisioning tenant, and no More actions button', async () => {
    stubRoutes(ok({ ...acme, status: 'provisioning' }))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(screen.queryByTestId('cell-tenant-delete')).not.toBeInTheDocument()
    expect(
      within(screen.getByTestId('page-actions')).queryByRole('button', { name: 'More actions' }),
    ).not.toBeInTheDocument()
  })

  it('offers Set quotas and Add a host as buttons on the heading of a provisioning tenant', async () => {
    stubRoutes(ok({ ...acme, status: 'provisioning' }))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    const actions = within(screen.getByTestId('page-actions'))
    expect(actions.getByRole('button', { name: 'Set quotas…' })).toBeInTheDocument()
    expect(actions.getByRole('button', { name: 'Add a host…' })).toBeInTheDocument()
  })

  it('adds a host and shows its TXT and CNAME records', async () => {
    const writes = stubWrites({ '/api/cell/tenants/acme/hosts': { status: 201, body: hostRequest } })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-add-host')
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Add a host to acme')
    const add = within(dialog).getByRole('button', { name: 'Add' })
    expect(add).toBeEnabled()
    await user.type(within(dialog).getByRole('textbox', { name: 'Host' }), 'portal.acme.test')
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

  it('loads the tenant again once a host is added', async () => {
    stubWrites({ '/api/cell/tenants/acme/hosts': { status: 201, body: hostRequest } })
    const tenantReads = () =>
      vi.mocked(fetch).mock.calls.filter(([input]) => (input as string) === '/api/cell/tenants/acme').length
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-add-host')
    const dialog = await screen.findByRole('dialog')
    expect(tenantReads()).toBe(1)
    await user.type(within(dialog).getByRole('textbox', { name: 'Host' }), 'portal.acme.test')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    await within(dialog).findByTestId('cell-tenant-host-records')
    await waitFor(() => expect(tenantReads()).toBe(2))
  })

  it('checks the host on click and sends nothing', async () => {
    const writes = stubWrites({ '/api/cell/tenants/acme/hosts': { status: 201, body: hostRequest } })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-add-host')
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    expect(await within(dialog).findByText('Enter a host.')).toBeInTheDocument()
    await waitFor(() => expect(within(dialog).getByRole('textbox', { name: 'Host' })).toHaveFocus())
    expect(writes).toHaveLength(0)
  })

  it('keeps the host dialog open with the title of a refusal', async () => {
    stubWrites({
      '/api/cell/tenants/acme/hosts': { status: 409, body: { title: 'Another tenant holds this host.' } },
    })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-add-host')
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Host' }), 'taken.example.com')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    const alert = await screen.findByTestId('cell-tenant-host-error')
    expect(alert).toHaveTextContent('The cell refused the request: Another tenant holds this host.')
    expect(
      alert.compareDocumentPosition(within(dialog).getByRole('textbox', { name: 'Host' })) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.queryByTestId('cell-tenant-host-records')).not.toBeInTheDocument()
  })

  it('reports a forbidden host request to the console', async () => {
    stubWrites({ '/api/cell/tenants/acme/hosts': { status: 403, body: { status: 403 } } })
    const user = userEvent.setup()
    const onRefused = renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-add-host')
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Host' }), 'portal.acme.test')
    await user.click(within(dialog).getByRole('button', { name: 'Add' }))

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('forbidden'))
  })

  it('shows the did-not-reach message when the quotas request fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubWrites({ '/api/cell/tenants/acme/quotas': fail(500) })
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-set-quotas')
    const dialog = await screen.findByRole('dialog')
    await user.click(
      within(within(dialog).getByRole('radiogroup', { name: 'Users' })).getByRole('radio', { name: /^No limit/ }),
    )
    await user.click(within(dialog).getByRole('button', { name: 'Save' }))

    const alert = await screen.findByTestId('cell-tenant-quotas-error')
    expect(alert).toHaveTextContent('The request did not reach the cell.')
    expect(
      alert.compareDocumentPosition(within(dialog).getByRole('radiogroup', { name: 'Users' })) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
  })

  it('keeps Delete disabled until the typed text equals the tenant id exactly', async () => {
    stubRoutes(ok(acme))
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-delete')
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Delete acme?')
    const deleteButton = within(dialog).getByRole('button', { name: /Delete$/ })
    const input = within(dialog).getByLabelText('Type acme to confirm')

    expect(input).toHaveAccessibleDescription(
      'Type the ID exactly, with the same case. Delete turns on when it matches.',
    )
    expect(deleteButton).toBeDisabled()
    await user.type(input, 'acm')
    expect(deleteButton).toBeDisabled()
    await user.clear(input)
    await user.type(input, 'ACME')
    expect(deleteButton).toBeDisabled()
    await user.clear(input)
    await user.type(input, 'acme')
    expect(deleteButton).toBeEnabled()
    await user.type(input, ' ')
    expect(deleteButton).toBeDisabled()
  })

  it('starts the typed text empty each time the delete dialog opens', async () => {
    stubRoutes(ok(acme))
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-delete')
    await user.type(within(await screen.findByRole('dialog')).getByLabelText('Type acme to confirm'), 'acme')
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    await chooseMore(user, 'cell-tenant-delete')
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByLabelText('Type acme to confirm')).toHaveValue('')
    expect(within(dialog).getByRole('button', { name: /Delete$/ })).toBeDisabled()
  })

  it('sends DELETE with an Idempotency-Key when the typed id is confirmed', async () => {
    const queued = { id: '11', kind: 'delete', tenantId: 'acme', state: 'queued', createdAt: job.createdAt }
    const { keys, sent } = stubActions(
      { 'DELETE /api/cell/tenants/acme': [{ status: 202, body: queued }] },
      { 'DELETE /api/cell/tenants/acme': 'deleting' },
    )
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await chooseMore(user, 'cell-tenant-delete')
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByLabelText('Type acme to confirm'), 'acme')
    await user.click(within(dialog).getByRole('button', { name: /Delete$/ }))

    await waitFor(() => expect(statusTag('Deleting')).toBeInTheDocument())
    expect(sent).toEqual(['DELETE /api/cell/tenants/acme'])
    expect(keys).toHaveLength(1)
    expect(keys[0]).toBeTruthy()
  })

  it('offers Cancel deletion in the More actions menu of a deleting tenant and posts the cancellation when it is confirmed', async () => {
    const queued = { id: '12', kind: 'cancel-deletion', tenantId: 'acme', state: 'queued', createdAt: job.createdAt }
    const { keys, sent } = stubActions(
      { 'POST /api/cell/tenants/acme/deletion/cancel': [{ status: 202, body: queued }] },
      { 'POST /api/cell/tenants/acme/deletion/cancel': 'suspended' },
      'deleting',
    )
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(within(screen.getByTestId('page-actions')).getByTestId('cell-tenant-export')).toBeInTheDocument()
    const menu = await openMore(user)
    expect(menu.queryByTestId('cell-tenant-suspend')).not.toBeInTheDocument()
    expect(menu.queryByTestId('cell-tenant-resume')).not.toBeInTheDocument()
    expect(menu.queryByTestId('cell-tenant-delete')).not.toBeInTheDocument()

    await user.click(menu.getByTestId('cell-tenant-cancel-deletion'))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Cancel the deletion of acme?')
    // The corner button of the dialog is also named Close, so the dismiss button is the second one.
    expect(within(dialog).getAllByRole('button', { name: 'Close' })).toHaveLength(2)
    expect(within(dialog).queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: /Cancel deletion$/ }))

    await waitFor(() => expect(statusTag('Suspended')).toBeInTheDocument())
    expect(sent).toEqual(['POST /api/cell/tenants/acme/deletion/cancel'])
    expect(keys[0]).toBeTruthy()
  })
})
