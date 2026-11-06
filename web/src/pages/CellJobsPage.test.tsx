import { render, screen, waitFor, within } from '@testing-library/react'
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
})
