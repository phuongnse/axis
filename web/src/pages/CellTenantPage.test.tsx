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
    if (input.startsWith('/api/cell/jobs')) {
      return new Response(JSON.stringify({ items: jobs }), { status: 200 })
    }
    return new Response(JSON.stringify(acme), { status: 200 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { accepts, fetchMock }
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

  it('offers Export for every status but provisioning', async () => {
    stubRoutes(ok({ ...acme, status: 'suspended' }))
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    expect(screen.getByTestId('cell-tenant-export')).toBeInTheDocument()
  })

  it('queues an export job with an Idempotency-Key after the confirmation names the tenant', async () => {
    const queued = { id: '11', kind: 'export', tenantId: 'acme', state: 'queued', createdAt: job.createdAt }
    const keys = stubActions(
      { '/api/cell/tenants/acme/exports': [{ status: 202, body: queued }] },
      { '/api/cell/tenants/acme/exports': 'active' },
    )
    const user = userEvent.setup()
    renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-export'))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Export acme?')
    await user.click(within(dialog).getByRole('button', { name: /Export$/ }))

    await waitFor(() => expect(keys).toHaveLength(1))
    expect(keys[0]).toBeTruthy()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('offers Download only with a succeeded export job', async () => {
    stubDownload([{ ...exportJob, state: 'failed' }, job])
    renderPage('/tenants/acme')

    await screen.findByTestId('cell-tenant')
    await screen.findByTestId('cell-jobs')
    expect(screen.queryByTestId('cell-tenant-download')).not.toBeInTheDocument()
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

    await user.click(await screen.findByTestId('cell-tenant-download'))
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

    await user.click(await screen.findByTestId('cell-tenant-download'))
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

    await user.click(await screen.findByTestId('cell-tenant-download'))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: /Download$/ }))

    expect(await screen.findByTestId('cell-tenant-download-error')).toHaveTextContent('The export has expired.')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(createObjectURL).not.toHaveBeenCalled()
  })

  it('reports a refused download to the console', async () => {
    stubDownload([exportJob], [new Response('{}', { status: 403 })])
    const user = userEvent.setup()
    const onRefused = renderPage('/tenants/acme')

    await user.click(await screen.findByTestId('cell-tenant-download'))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: /Download$/ }))

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('forbidden'))
  })
})
