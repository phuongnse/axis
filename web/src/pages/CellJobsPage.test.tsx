import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Outlet, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { CellJob } from '../platform/cell'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { CellJobsPage } from './CellJobsPage'
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

const failed: CellJob = {
  id: '2',
  kind: 'create',
  tenantId: 'acme',
  state: 'failed',
  createdAt: '2026-11-04T10:00:00Z',
  steps: [
    { name: 'database', state: 'succeeded', error: null },
    { name: 'realm', state: 'failed', error: 'The realm already exists.' },
  ],
  error: 'The job stopped at realm.',
}
const succeeded: CellJob = {
  id: '1',
  kind: 'suspend',
  tenantId: 'beta',
  state: 'succeeded',
  createdAt: '2026-11-03T10:00:00Z',
  steps: [{ name: 'suspend', state: 'succeeded', error: null }],
  error: null,
}

function stubJobs(status: number, items: CellJob[] = []) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(status === 200 ? { items } : { status }), { status })),
  )
}

/**
 * Answers the job list, then `202` with the queued job for `POST /api/cell/jobs/2/run-again`, and the queued job for
 * its read. It records each request and the Idempotency-Key of the writes.
 */
function stubRunAgain(items: CellJob[], again: { status: number; body?: object }[]) {
  const sent: string[] = []
  const keys: (string | null)[] = []
  const queued = { id: '3', kind: 'create', tenantId: 'acme', state: 'queued', createdAt: '2026-11-05T10:00:00Z' }
  const answers = [...again]
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const request = `${init?.method ?? 'GET'} ${String(input)}`
      sent.push(request)
      if (request === 'GET /api/cell/jobs') {
        return new Response(JSON.stringify({ items }), { status: 200 })
      }
      if (request === 'POST /api/cell/jobs/2/run-again') {
        keys.push(new Headers(init?.headers).get('Idempotency-Key'))
        const answer = answers.shift() ?? { status: 202, body: queued }
        return new Response(JSON.stringify(answer.body ?? queued), { status: answer.status })
      }
      return new Response(JSON.stringify({ ...queued, steps: [], error: null }), { status: 200 })
    }),
  )
  return { sent, keys }
}

function renderPage(onRefused = vi.fn()) {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <PlatformContext.Provider value={platform}>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <Routes>
              <Route element={<Outlet context={{ onRefused }} />}>
                <Route index element={<CellJobsPage />} />
              </Route>
            </Routes>
          </TextProvider>
        </PlatformContext.Provider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
  return onRefused
}

describe('CellJobsPage', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('lists the jobs in the order the API answers, with steps and errors', async () => {
    stubJobs(200, [failed, succeeded])
    renderPage()

    const table = await screen.findByTestId('cell-jobs')
    const rows = within(table).getAllByRole('row')
    // The header row comes first.
    expect(rows).toHaveLength(3)
    expect(rows[1]).toHaveTextContent('acme')
    expect(rows[1]).toHaveTextContent('create')
    expect(rows[1]).toHaveTextContent('Failed')
    expect(rows[2]).toHaveTextContent('beta')
    expect(rows[2]).toHaveTextContent('Succeeded')
    expect(within(rows[1]).getByTestId('cell-job-step-error')).toHaveTextContent('The realm already exists.')
    expect(within(rows[1]).getByTestId('cell-job-error')).toHaveTextContent('The job stopped at realm.')
    expect(within(rows[1]).getAllByTestId('cell-job-step')).toHaveLength(2)
    expect(within(rows[2]).queryByTestId('cell-job-step-error')).not.toBeInTheDocument()
  })

  it('says that no step has run yet for a job with no steps', async () => {
    stubJobs(200, [{ ...succeeded, state: 'queued', steps: [] }, failed])
    renderPage()

    const rows = within(await screen.findByTestId('cell-jobs')).getAllByRole('row')
    expect(within(rows[1]).getByTestId('cell-job-no-steps')).toHaveTextContent('No step has run yet.')
    expect(within(rows[1]).queryByTestId('cell-job-step')).not.toBeInTheDocument()
    expect(within(rows[2]).queryByTestId('cell-job-no-steps')).not.toBeInTheDocument()
  })

  it('says so when the cell has no jobs', async () => {
    stubJobs(200)
    renderPage()

    expect(await screen.findByText('The cell has no jobs yet.')).toBeInTheDocument()
  })

  it('reports a refusal to the console', async () => {
    stubJobs(401)
    const onRefused = renderPage()

    await waitFor(() => expect(onRefused).toHaveBeenCalledWith('unauthorized'))
  })

  it('shows an error when the jobs fail to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubJobs(500)
    renderPage()

    expect(await screen.findByTestId('cell-jobs-error')).toHaveTextContent('The jobs could not be loaded.')
  })

  it('offers Run again on a failed job only, and its confirmation names the job and the tenant', async () => {
    stubRunAgain([failed, succeeded], [])
    const user = userEvent.setup()
    renderPage()

    const table = await screen.findByTestId('cell-jobs')
    const rows = within(table).getAllByRole('row')
    expect(within(rows[1]).getByRole('button', { name: 'Run again' })).toBeInTheDocument()
    expect(within(rows[2]).queryByRole('button', { name: 'Run again' })).not.toBeInTheDocument()

    await user.click(within(rows[1]).getByRole('button', { name: 'Run again' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Run the create job of acme again?')
    expect(dialog).toHaveTextContent('Steps that succeeded are not run again.')
  })

  it('offers no Run again on a failed purge job', async () => {
    stubRunAgain([{ ...failed, kind: 'purge' }], [])
    renderPage()

    const table = await screen.findByTestId('cell-jobs')
    expect(within(table).queryByRole('button', { name: 'Run again' })).not.toBeInTheDocument()
  })

  it('posts the run again with an Idempotency-Key and lists the new queued job first', async () => {
    const { sent, keys } = stubRunAgain([failed, succeeded], [])
    const user = userEvent.setup()
    renderPage()

    const table = await screen.findByTestId('cell-jobs')
    await user.click(within(within(table).getAllByRole('row')[1]).getByRole('button', { name: 'Run again' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Run again' }))

    await waitFor(() => expect(sent).toContain('POST /api/cell/jobs/2/run-again'))
    expect(keys).toHaveLength(1)
    expect(keys[0]).toBeTruthy()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const rows = within(screen.getByTestId('cell-jobs')).getAllByRole('row')
    expect(rows).toHaveLength(4)
    expect(rows[1]).toHaveTextContent('acme')
    expect(rows[1]).toHaveTextContent('Queued')
    expect(rows[2]).toHaveTextContent('Failed')
    await waitFor(() => expect(sent).toContain('GET /api/cell/jobs/3'))
  })

  it('keeps the Idempotency-Key for a retry after a 5xx and shows the refusal of a 409', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const { keys } = stubRunAgain(
      [failed],
      [{ status: 500 }, { status: 409, body: { title: 'The tenant has a newer job of this kind.' } }],
    )
    const user = userEvent.setup()
    renderPage()

    const table = await screen.findByTestId('cell-jobs')
    await user.click(within(within(table).getAllByRole('row')[1]).getByRole('button', { name: 'Run again' }))
    const dialog = await screen.findByRole('dialog')
    await user.click(within(dialog).getByRole('button', { name: 'Run again' }))
    expect(await screen.findByTestId('cell-jobs-action-error')).toHaveTextContent('Try again.')
    await user.click(await within(dialog).findByRole('button', { name: /Run again$/ }))

    expect(await screen.findByTestId('cell-jobs-action-error')).toHaveTextContent(
      'The cell refused the request: The tenant has a newer job of this kind.',
    )
    expect(keys).toHaveLength(2)
    expect(keys[1]).toBe(keys[0])
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })
})
