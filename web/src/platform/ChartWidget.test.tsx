import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ChartProps } from './Chart'
import { ChartWidget } from './ChartWidget'
import type { ChartMetadata, WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'

// The chart library is not under test here. The mock records what the widget hands to the chart.
const drawn = vi.hoisted(() => ({ props: [] as unknown[] }))
vi.mock('./Chart', () => ({
  Chart: (props: ChartProps) => {
    drawn.props.push(props)
    return <div role="img" aria-label={props.title} />
  },
}))

const rowsPath = '/api/apps/E2eApp/data-sources/NotesByCategory/rows'

const bar: ChartMetadata = {
  kind: 'bar',
  titleKey: 'chart.notes',
  category: 'category',
  measures: [
    { name: 'notes', labelKey: 'chart.count' },
    { name: 'amountSum', labelKey: 'chart.amount' },
  ],
}

function chartWidget(chart: ChartMetadata): WidgetMetadata {
  return {
    type: 'chart',
    entity: null,
    form: null,
    tasksPath: null,
    process: null,
    instancesPath: null,
    deadLettersPath: null,
    actions: [],
    rowActions: [],
    toolbarActions: [],
    parameters: {},
    access: null,
    detail: null,
    kpi: null,
    chart,
    dataSource: {
      name: 'NotesByCategory',
      rowsPath,
      entity: 'Note',
      parameters: [],
      pageSize: 100,
      search: false,
      columns: [
        { name: 'category', type: 'text', labelKey: 'chart.category', values: null, target: null },
        { name: 'notes', type: 'integer', labelKey: null, values: null, target: null },
        { name: 'amountSum', type: 'decimal', labelKey: null, values: null, target: null },
      ],
    },
  }
}

const texts = {
  'shell.chart.loading': 'Loading…',
  'shell.chart.loadFailed': 'The chart could not be loaded.',
  'shell.chart.empty': 'No data to show',
  'shell.chart.noValue': 'No value',
  'shell.chart.truncated': 'Only the first 100 groups show.',
  'chart.notes': 'Notes by category',
  'chart.count': 'Notes',
  'chart.amount': 'Amount total',
  'chart.category': 'Category',
}
const catalogs = [{ texts, fallbackTexts: texts }]

// Written by hand, as the server writes it, so the decimal keeps its source text.
function groups(rows: string[], totalCount = rows.length) {
  const items = rows.map(
    (values, index) =>
      `{"id":"6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f${index}","version":1,"values":${values},"labels":{}}`,
  )
  return `{"items":[${items.join(',')}],"page":1,"pageSize":100,"totalCount":${totalCount}}`
}

const twoGroups = groups([
  '{"category":"idea","notes":3,"amountSum":12.50}',
  '{"category":"todo","notes":5,"amountSum":null}',
])

function stubRows(body: string, status = 200) {
  const fetchMock = vi.fn(async (_input: string) => new Response(body, { status }))
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

/** A chart widget whose data source has two parameters, of which `parameters` binds only `inCategory`. */
function boundChartWidget(): WidgetMetadata {
  const widget = chartWidget(bar)
  const parameter = (name: string) => ({
    name,
    type: 'text' as const,
    required: false,
    labelKey: null,
    values: null,
    target: null,
  })
  widget.dataSource!.parameters = [parameter('inCategory'), parameter('other')]
  widget.parameters = { inCategory: 'category' }
  return widget
}

function renderChart(chart: ChartMetadata = bar, widget = chartWidget(chart), pageValues?: Record<string, string>) {
  return render(
    <MemoryRouter>
      <TextProvider catalogs={catalogs} development={false}>
        <ChartWidget widget={widget} locale="en" pageValues={pageValues} />
      </TextProvider>
    </MemoryRouter>,
  )
}

function lastProps(): ChartProps {
  return drawn.props[drawn.props.length - 1] as ChartProps
}

describe('ChartWidget', () => {
  beforeEach(() => {
    drawn.props.length = 0
  })

  it('draws the categories and the labelled series, and asks for the groups once', async () => {
    const requested = stubRows(twoGroups)

    renderChart()

    await screen.findByRole('img', { name: 'Notes by category' })
    expect(lastProps()).toMatchObject({
      title: 'Notes by category',
      kind: 'bar',
      categories: ['idea', 'todo'],
      series: [
        { label: 'Notes', values: [3, 5] },
        { label: 'Amount total', values: [12.5, null] },
      ],
    })
    expect(requested()).toEqual([`${rowsPath}?pageSize=100`])
  })

  it('asks for the groups with the value of each bound page parameter under the data source parameter name', async () => {
    const requested = stubRows(twoGroups)

    renderChart(bar, boundChartWidget(), { category: 'idea', other: 'ignored' })

    await screen.findByRole('img', { name: 'Notes by category' })
    expect(requested()).toEqual([`${rowsPath}?inCategory=idea&pageSize=100`])
  })

  it('leaves out a bound parameter whose page parameter has no value', async () => {
    const requested = stubRows(twoGroups)

    renderChart(bar, boundChartWidget(), {})

    await screen.findByRole('img', { name: 'Notes by category' })
    expect(requested()).toEqual([`${rowsPath}?pageSize=100`])
  })

  it('asks again when the page parameter value changes', async () => {
    const requested = stubRows(twoGroups)
    const widget = boundChartWidget()
    const view = renderChart(bar, widget, { category: 'idea' })
    await screen.findByRole('img', { name: 'Notes by category' })

    view.rerender(
      <MemoryRouter>
        <TextProvider catalogs={catalogs} development={false}>
          <ChartWidget widget={widget} locale="en" pageValues={{ category: 'todo' }} />
        </TextProvider>
      </MemoryRouter>,
    )

    await waitFor(() =>
      expect(requested()).toEqual([
        `${rowsPath}?inCategory=idea&pageSize=100`,
        `${rowsPath}?inCategory=todo&pageSize=100`,
      ]),
    )
  })

  it('passes the kind of the chart on', async () => {
    stubRows(twoGroups)

    renderChart({ ...bar, kind: 'pie', measures: [bar.measures[0]] })

    await screen.findByRole('img', { name: 'Notes by category' })
    expect(lastProps()).toMatchObject({ kind: 'pie', series: [{ label: 'Notes', values: [3, 5] }] })
  })

  it('has a hidden data table with the category and each measure as columns', async () => {
    stubRows(twoGroups)

    renderChart()

    const table = await screen.findByRole('table', { name: 'Notes by category' })
    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['Category', 'Notes', 'Amount total'])
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => Array.from(row.children).map((cell) => cell.textContent))).toEqual([
      ['idea', '3', '12.50'],
      ['todo', '5', ''],
    ])
  })

  it('names the category column after its name when the data source gives it no label', async () => {
    stubRows(twoGroups)
    const widget = chartWidget(bar)
    widget.dataSource!.columns[0].labelKey = null

    render(
      <MemoryRouter>
        <TextProvider catalogs={catalogs} development={false}>
          <ChartWidget widget={widget} locale="en" />
        </TextProvider>
      </MemoryRouter>,
    )

    const table = await screen.findByRole('table', { name: 'Notes by category' })
    expect(within(table).getAllByRole('columnheader')[0]).toHaveTextContent('category')
  })

  it('shows the no-value text for a null category', async () => {
    stubRows(groups(['{"category":null,"notes":2,"amountSum":1}', '{"category":"idea","notes":1,"amountSum":1}']))

    renderChart()

    await screen.findByRole('img', { name: 'Notes by category' })
    expect(lastProps().categories).toEqual(['No value', 'idea'])
    expect(
      within(screen.getByRole('table'))
        .getAllByRole('rowheader')
        .map((cell) => cell.textContent),
    ).toEqual(['No value', 'idea'])
  })

  it('does not note anything when the data source has 100 groups or fewer', async () => {
    stubRows(twoGroups)

    renderChart()

    await screen.findByRole('img', { name: 'Notes by category' })
    expect(screen.queryByTestId('chart-truncated')).not.toBeInTheDocument()
  })

  it('notes that only the first 100 groups show when the data source has more', async () => {
    stubRows(groups(['{"category":"idea","notes":3,"amountSum":1}'], 101))

    renderChart()

    expect(await screen.findByTestId('chart-truncated')).toHaveTextContent('Only the first 100 groups show.')
  })

  it('shows the empty text when the data source has no group', async () => {
    stubRows(groups([]))

    renderChart()

    expect(await screen.findByTestId('chart-empty')).toHaveTextContent('No data to show')
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('shows the loading text and is busy until the groups arrive', async () => {
    stubRows(twoGroups)

    renderChart()

    expect(screen.getByTestId('chart-widget')).toHaveAttribute('aria-busy', 'true')
    expect(screen.getByText('Loading…')).toBeInTheDocument()
    await screen.findByRole('img', { name: 'Notes by category' })
    expect(screen.getByTestId('chart-widget')).toHaveAttribute('aria-busy', 'false')
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument()
  })

  it('shows the error text when the request fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    stubRows('', 500)

    renderChart()

    expect(await screen.findByTestId('chart-error')).toHaveTextContent('The chart could not be loaded.')
    await waitFor(() => expect(screen.queryByRole('img')).not.toBeInTheDocument())
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
})
