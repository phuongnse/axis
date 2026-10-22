import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { KpiWidget } from './KpiWidget'
import type { KpiMetadata, NavigateAction, WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'

const rowsPath = '/api/apps/E2eApp/data-sources/NoteTotals/rows'

const showNotes: NavigateAction = {
  type: 'navigate',
  page: 'Notes',
  labelKey: 'kpi.showNotes',
  parameters: {},
  record: null,
  opensForm: false,
}

const withComparison: KpiMetadata = {
  measure: 'openCount',
  labelKey: 'kpi.open',
  comparison: { measure: 'urgentCount', labelKey: 'kpi.urgent' },
  navigate: null,
}

function kpiWidget(kpi: KpiMetadata): WidgetMetadata {
  return {
    type: 'kpi',
    entity: null,
    form: null,
    tasksPath: null,
    process: null,
    instancesPath: null,
    actions: [],
    rowActions: [],
    toolbarActions: [],
    parameters: {},
    access: null,
    kpi,
    dataSource: {
      name: 'NoteTotals',
      rowsPath,
      entity: 'Note',
      parameters: [],
      pageSize: 1,
      columns: [
        { name: 'openCount', type: 'decimal', labelKey: null, values: null, target: null },
        { name: 'urgentCount', type: 'integer', labelKey: null, values: null, target: null },
      ],
    },
  }
}

// Written by hand, as the server writes it, so the decimal keeps its source text.
function totalRow(values: string) {
  return `{"items":[{"id":"6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7","version":1,"values":${values},"labels":{}}],"page":1,"pageSize":1,"totalCount":1}`
}

const texts = {
  'shell.kpi.empty': 'No value',
  'shell.kpi.loading': 'Loading…',
  'shell.kpi.loadFailed': 'The value could not be loaded.',
  'kpi.open': 'Open',
  'kpi.urgent': 'Urgent',
  'kpi.showNotes': 'Show notes',
}
const catalogs = [{ texts, fallbackTexts: texts }]

function stubRows(body: string, status = 200) {
  const fetchMock = vi.fn(async (_input: string) => new Response(body, { status }))
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

function CurrentLocation() {
  const location = useLocation()
  return <output data-testid="path">{location.pathname + location.search}</output>
}

function renderKpi(kpi: KpiMetadata, pageValues?: Record<string, string>) {
  return render(
    <MemoryRouter initialEntries={['/e2e/notetotals']}>
      <TextProvider catalogs={catalogs} development={false}>
        <KpiWidget sitePath="e2e" widget={kpiWidget(kpi)} locale="en" pageValues={pageValues} />
      </TextProvider>
      <CurrentLocation />
    </MemoryRouter>,
  )
}

describe('KpiWidget', () => {
  it('shows the labelled measure and comparison as the server wrote them', async () => {
    const requested = stubRows(totalRow('{"openCount":12.50,"urgentCount":3}'))

    renderKpi(withComparison)

    await waitFor(() => expect(screen.getByTestId('kpi-value')).toHaveTextContent(/^Open: 12\.50$/))
    expect(screen.getByTestId('kpi-comparison')).toHaveTextContent(/^Urgent: 3$/)
    expect(requested()).toEqual([rowsPath])
  })

  it('shows one line without a comparison', async () => {
    stubRows(totalRow('{"openCount":12.50,"urgentCount":3}'))

    renderKpi({ ...withComparison, comparison: null })

    await waitFor(() => expect(screen.getByTestId('kpi-value')).toHaveTextContent(/^Open: 12\.50$/))
    expect(screen.queryByTestId('kpi-comparison')).not.toBeInTheDocument()
  })

  it('shows the empty text for a null measure', async () => {
    stubRows(totalRow('{"openCount":null,"urgentCount":3}'))

    renderKpi(withComparison)

    await waitFor(() => expect(screen.getByTestId('kpi-value')).toHaveTextContent(/^Open: No value$/))
    expect(screen.getByTestId('kpi-comparison')).toHaveTextContent(/^Urgent: 3$/)
  })

  it('shows the empty text when the data source returns no row', async () => {
    stubRows('{"items":[],"page":1,"pageSize":1,"totalCount":0}')

    renderKpi(withComparison)

    await waitFor(() => expect(screen.getByTestId('kpi-value')).toHaveTextContent(/^Open: No value$/))
  })

  it('shows the loading text and is busy until the row arrives', async () => {
    stubRows(totalRow('{"openCount":1,"urgentCount":2}'))

    renderKpi(withComparison)

    expect(screen.getByTestId('kpi-tile')).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByTestId('kpi-value')).toBeInTheDocument())
    expect(screen.getByTestId('kpi-tile')).toHaveAttribute('aria-busy', 'false')
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument()
  })

  it('shows the error text when the request fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    stubRows('', 500)

    renderKpi(withComparison)

    expect(await screen.findByTestId('kpi-error')).toHaveTextContent('The value could not be loaded.')
    expect(screen.queryByTestId('kpi-value')).not.toBeInTheDocument()
  })

  it('is a link to the target page when it has a navigate action', async () => {
    stubRows(totalRow('{"openCount":12.50,"urgentCount":3}'))
    const user = userEvent.setup()

    renderKpi({ ...withComparison, navigate: showNotes })

    const link = await screen.findByRole('link', { name: /Open: 12\.50/ })
    expect(link).toHaveTextContent('Show notes')
    await user.click(link)
    expect(screen.getByTestId('path')).toHaveTextContent('/e2e/notes')
  })

  it('gives the navigate action the page values', async () => {
    stubRows(totalRow('{"openCount":12.50,"urgentCount":3}'))

    renderKpi({ ...withComparison, navigate: { ...showNotes, parameters: { kind: 'region' } } }, { region: 'north' })

    expect(await screen.findByRole('link')).toHaveAttribute('href', '/e2e/notes?kind=north')
  })

  it('is plain without a navigate action', async () => {
    stubRows(totalRow('{"openCount":12.50,"urgentCount":3}'))

    renderKpi(withComparison)

    await waitFor(() => expect(screen.getByTestId('kpi-value')).toBeInTheDocument())
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('is plain when the navigate expressions cannot be worked out', async () => {
    stubRows(totalRow('{"openCount":12.50,"urgentCount":3}'))

    renderKpi({ ...withComparison, navigate: { ...showNotes, parameters: { kind: 'missing' } } })

    await waitFor(() => expect(screen.getByTestId('kpi-value')).toBeInTheDocument())
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
