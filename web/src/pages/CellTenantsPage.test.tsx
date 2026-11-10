import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { CellJob, CellTenant } from '../platform/cell'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { CellTenantsPage } from './CellTenantsPage'
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

const hoursAgo = (hours: number) => new Date(Date.now() - hours * 60 * 60 * 1000).toISOString()
const noUse = { limit: null, used: null }
const noUsage = { storage: noUse, users: noUse, applications: noUse, emailsPerDay: noUse }

const acme: CellTenant = {
  id: 'acme',
  status: 'active',
  application: 'crm',
  hosts: ['acme.example.com', 'portal.acme.test'],
  failedWork: { instances: 2, unknownCalls: 0, deadLetters: 1 },
  usage: {
    storage: { limit: 1024 ** 3 * 10, used: 1024 ** 2 * 512 },
    users: { limit: 50, used: null },
    applications: { limit: null, used: 3 },
    emailsPerDay: { limit: 1000, used: 1200 },
  },
  newestArchiveAt: hoursAgo(30),
}
const beta: CellTenant = {
  id: 'beta',
  status: 'suspended',
  application: null,
  hosts: ['beta.example.com'],
  failedWork: null,
  usage: noUsage,
  newestArchiveAt: null,
}
const gamma: CellTenant = {
  id: 'gamma',
  status: 'readOnly',
  application: 'hr',
  hosts: ['gamma.example.com'],
  failedWork: { instances: 0, unknownCalls: 0, deadLetters: 0 },
  usage: noUsage,
  newestArchiveAt: hoursAgo(2),
}

function stubTenants(status: number, items: CellTenant[] = []) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(status === 200 ? { items } : { status }), { status })),
  )
}

function renderPage(onRefused = vi.fn()) {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <PlatformContext.Provider value={platform}>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <Routes>
              <Route element={<Outlet context={{ onRefused }} />}>
                <Route index element={<CellTenantsPage />} />
              </Route>
            </Routes>
          </TextProvider>
        </PlatformContext.Provider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
  return onRefused
}

interface Post {
  key: string | null
  contentType: string | null
  body: unknown
}

const queued = { id: '7', kind: 'create', tenantId: 'delta', state: 'queued', createdAt: hoursAgo(0) }

function createJob(state: CellJob['state']): CellJob {
  return {
    ...queued,
    state,
    steps: ['tenant', 'database', 'realm', 'application', 'invite'].map((name) => ({
      name,
      state: state === 'succeeded' ? 'succeeded' : 'pending',
      error: null,
    })),
    error: null,
  }
}

/**
 * Answers the create dialog's page. Each POST takes the next of `posts`, and each job read the next of `reads`, the
 * last one for good. `lists` counts the reads of the tenant list.
 */
function stubCreate(posts: { status: number; body: unknown }[], reads: CellJob[] = []) {
  const sent: Post[] = []
  const counts = { lists: 0 }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        const headers = new Headers(init.headers)
        sent.push({
          key: headers.get('Idempotency-Key'),
          contentType: headers.get('Content-Type'),
          body: JSON.parse(init.body as string),
        })
        const answer = posts.shift()!
        return new Response(JSON.stringify(answer.body), { status: answer.status })
      }
      if (input.startsWith('/api/cell/jobs/')) {
        const job = reads.length > 1 ? reads.shift()! : reads[0]
        return new Response(JSON.stringify(job), { status: 200 })
      }
      counts.lists += 1
      return new Response(JSON.stringify({ items: [acme] }), { status: 200 })
    }),
  )
  return { sent, counts }
}

async function openDialog(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByTestId('cell-tenants-create'))
  return screen.findByRole('dialog')
}

async function fillDialog(user: ReturnType<typeof userEvent.setup>, dialog: HTMLElement, id = 'delta') {
  await user.clear(within(dialog).getByLabelText('Tenant ID'))
  await user.type(within(dialog).getByLabelText('Tenant ID'), id)
  await user.clear(within(dialog).getByLabelText('Application'))
  await user.type(within(dialog).getByLabelText('Application'), ' approvals ')
  await user.clear(within(dialog).getByLabelText("Administrator's email"))
  await user.type(within(dialog).getByLabelText("Administrator's email"), 'admin@delta.test')
}

// The dialog's corner icon has the same name, so the footer button is the last.
const closeButton = (dialog: HTMLElement) => within(dialog).getAllByRole('button', { name: 'Close' }).at(-1)!
const createButton = (dialog: HTMLElement) => within(dialog).getByRole('button', { name: 'Create' })

function dataRows() {
  return within(screen.getByTestId('cell-tenants')).getAllByRole('row').slice(1)
}

function rowIds() {
  return dataRows().map((row) => within(row).getAllByRole('cell')[0].textContent)
}

describe('CellTenantsPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists one row per tenant, with dashes for the failed work not yet counted', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    const rows = dataRows()
    expect(rows).toHaveLength(3)
    expect(rows[0]).toHaveTextContent('Active')
    expect(rows[0]).toHaveTextContent('crm')
    expect(rows[0]).toHaveTextContent('acme.example.com, portal.acme.test')
    expect(
      within(rows[0])
        .getAllByRole('cell')
        .slice(4, 7)
        .map((cell) => cell.textContent),
    ).toEqual(['2', '0', '1'])
    expect(
      within(rows[1])
        .getAllByRole('cell')
        .slice(4, 7)
        .map((cell) => cell.textContent),
    ).toEqual(['—', '—', '—'])
    expect(within(rows[0]).getByRole('link', { name: 'acme' })).toHaveAttribute('href', '/tenants/acme')
  })

  it('shows the usage against the limits, with a dash for a count not yet filled', async () => {
    stubTenants(200, [acme, beta])
    renderPage()

    await screen.findByTestId('cell-tenants')
    const rows = dataRows()
    expect(
      within(rows[0])
        .getAllByRole('cell')
        .slice(7, 11)
        .map((cell) => cell.textContent),
    ).toEqual(['512 MB of 10 GB', '—', '3, no limit', '1,200 of 1,000'])
    expect(
      within(rows[1])
        .getAllByRole('cell')
        .slice(7, 11)
        .map((cell) => cell.textContent),
    ).toEqual(['—', '—', '—', '—'])
  })

  it('marks an archive more than 26 hours old and no other, and shows a dash for none', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    const rows = dataRows()
    expect(within(rows[0]).getByTestId('cell-tenants-archive-stale')).toHaveTextContent('Overdue')
    expect(within(rows[1]).queryByTestId('cell-tenants-archive-stale')).toBeNull()
    expect(within(rows[1]).getAllByRole('cell')[11]).toHaveTextContent('—')
    expect(within(rows[2]).queryByTestId('cell-tenants-archive-stale')).toBeNull()
    expect(within(rows[2]).getAllByRole('cell')[11]).not.toHaveTextContent('—')
  })

  it('narrows the rows by id, ignoring case', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    await userEvent.type(screen.getByLabelText('Search by ID or host'), 'BETA')

    expect(rowIds()).toEqual(['beta'])
  })

  it('narrows the rows by any host', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    await userEvent.type(screen.getByLabelText('Search by ID or host'), 'portal')

    expect(rowIds()).toEqual(['acme'])
  })

  it('keeps only the tenants with the chosen status', async () => {
    stubTenants(200, [acme, beta, gamma])
    renderPage()

    await screen.findByTestId('cell-tenants')
    await userEvent.click(screen.getByRole('combobox', { name: 'Status' }))
    await userEvent.click(await screen.findByRole('option', { name: 'Read only' }))

    expect(rowIds()).toEqual(['gamma'])
  })

  it('reports a refusal to the console', async () => {
    stubTenants(401)
    const onRefused = renderPage()

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('unauthorized'))
  })

  it('shows an error when the tenants fail to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubTenants(500)
    renderPage()

    expect(await screen.findByTestId('cell-tenants-error')).toHaveTextContent('The tenants could not be loaded.')
  })

  it('sends the fields trimmed as JSON with an Idempotency-Key', async () => {
    const { sent } = stubCreate([{ status: 202, body: queued }], [createJob('running')])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)
    await fillDialog(user, dialog)
    await user.click(createButton(dialog))

    await within(dialog).findByTestId('cell-jobs')
    expect(sent).toHaveLength(1)
    expect(sent[0].key).toBeTruthy()
    expect(sent[0].contentType).toBe('application/json')
    expect(sent[0].body).toEqual({ id: 'delta', application: 'approvals', administrator: 'admin@delta.test' })
  })

  it('does not send a request until every field is valid', async () => {
    const { sent } = stubCreate([])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)
    await user.type(within(dialog).getByLabelText('Tenant ID'), 'Not Valid')
    await user.click(createButton(dialog))

    expect(await within(dialog).findAllByText(/required|not match|valid/i)).not.toHaveLength(0)
    expect(sent).toHaveLength(0)
  })

  it('gives the tenant id, the application and the administrator their help', async () => {
    stubCreate([])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)

    expect(within(dialog).getByLabelText('Tenant ID')).toHaveAccessibleDescription(
      '1 to 50 lower-case letters, digits and hyphens, such as acme.',
    )
    expect(within(dialog).getByLabelText('Application')).toHaveAccessibleDescription(
      'The ID of an installed application template, such as approvals.',
    )
    expect(within(dialog).getByLabelText("Administrator's email")).toHaveAccessibleDescription(
      "The tenant's first administrator, who gets an invitation at this address.",
    )
  })

  it('says what to enter in each empty field and sends nothing', async () => {
    const { sent } = stubCreate([])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)
    await user.click(createButton(dialog))

    expect(await within(dialog).findByText('Enter the tenant ID.')).toBeInTheDocument()
    expect(within(dialog).getByText("Enter the application's ID.")).toBeInTheDocument()
    expect(within(dialog).getByText("Enter the administrator's email.")).toBeInTheDocument()
    expect(sent).toHaveLength(0)
  })

  it('shows the title of a refusal in the open dialog', async () => {
    stubCreate([{ status: 409, body: { title: 'A tenant of the cell already has this id.' } }])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)
    await fillDialog(user, dialog)
    await user.click(createButton(dialog))

    expect(await within(dialog).findByTestId('cell-create-error')).toHaveTextContent(
      'A tenant of the cell already has this id.',
    )
    expect(within(dialog).queryByTestId('cell-jobs')).not.toBeInTheDocument()
  })

  it('sends the same Idempotency-Key for a retry after a 5xx', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const { sent } = stubCreate([
      { status: 500, body: {} },
      { status: 202, body: queued },
    ])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)
    await fillDialog(user, dialog)
    await user.click(createButton(dialog))
    expect(await within(dialog).findByTestId('cell-create-error')).toHaveTextContent('Try again.')
    await user.click(createButton(dialog))

    await within(dialog).findByTestId('cell-jobs')
    expect(sent).toHaveLength(2)
    expect(sent[0].key).toBeTruthy()
    expect(sent[1].key).toBe(sent[0].key)
  })

  it('sends a new Idempotency-Key when a field changed after a 5xx', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const { sent } = stubCreate([
      { status: 500, body: {} },
      { status: 202, body: queued },
    ])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)
    await fillDialog(user, dialog)
    await user.click(createButton(dialog))
    await within(dialog).findByTestId('cell-create-error')
    await user.type(within(dialog).getByLabelText('Tenant ID'), '2')
    await user.click(createButton(dialog))

    await within(dialog).findByTestId('cell-jobs')
    expect(sent).toHaveLength(2)
    expect(sent[1].body).toEqual({ id: 'delta2', application: 'approvals', administrator: 'admin@delta.test' })
    expect(sent[1].key).not.toBe(sent[0].key)
  })

  it('follows the job until it ends and then loads the list again', async () => {
    const { counts } = stubCreate([{ status: 202, body: queued }], [createJob('running'), createJob('succeeded')])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)
    await fillDialog(user, dialog)
    await user.click(createButton(dialog))

    const jobs = await within(dialog).findByTestId('cell-jobs')
    expect(jobs).toHaveTextContent('create')
    expect(counts.lists).toBe(1)
    await waitFor(() => expect(jobs).toHaveTextContent('Succeeded'), { timeout: 5000 })
    await waitFor(() => expect(counts.lists).toBe(2))
    expect(within(dialog).queryByRole('button', { name: 'Create' })).not.toBeInTheDocument()
    await user.click(closeButton(dialog))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(counts.lists).toBe(2)
  })

  it('loads the list again when the dialog closes before the job ends', async () => {
    const { counts } = stubCreate([{ status: 202, body: queued }], [createJob('running')])
    const user = userEvent.setup()
    renderPage()

    const dialog = await openDialog(user)
    await fillDialog(user, dialog)
    await user.click(createButton(dialog))
    await within(dialog).findByTestId('cell-jobs')
    await user.click(closeButton(dialog))

    await waitFor(() => expect(counts.lists).toBe(2))
  })

  it('reports a refusal of the create to the console', async () => {
    stubCreate([{ status: 403, body: {} }])
    const user = userEvent.setup()
    const onRefused = renderPage()

    const dialog = await openDialog(user)
    await fillDialog(user, dialog)
    await user.click(createButton(dialog))

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('forbidden'))
  })
})
