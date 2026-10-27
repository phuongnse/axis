/// <reference types="node" />
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  chartWidget,
  detailWidget,
  fields,
  formWidget,
  kpiWidget,
  locale,
  requestId,
  routes,
  sitePath,
  tableWidget,
} from '../designSystem/fixtures'
import type { ChartProps } from './Chart'
import { FilterBar } from './FilterBar'
import { PageLeaf } from './PageLeaf'
import type { DataSourceParameter, EntityWidgetMetadata, FieldMetadata, WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'

// The chart library is not under test here. The mock records what the widget hands to the chart.
const drawn = vi.hoisted(() => ({ props: [] as unknown[] }))
vi.mock('./Chart', () => ({
  Chart: (props: ChartProps) => {
    drawn.props.push(props)
    return <div role="img" aria-label={props.title} />
  },
}))

const sources = import.meta.glob('./PageLeaf.tsx', { query: '?raw', import: 'default', eager: true }) as Record<
  string,
  string
>
const widgetTypes = [...sources['./PageLeaf.tsx'].matchAll(/case '(\w+)':/g)].map(([, type]) => type)

// Every widget type that PageLeaf draws says whether it shows enum values. A new type fails the
// test until it is classified, and a type that shows enums must show their labels.
const enumDisplay: Record<string, 'labels' | 'none'> = {
  table: 'labels',
  form: 'labels',
  detail: 'labels',
  kpi: 'labels',
  chart: 'labels',
  // The inbox lists tasks, with the step's label and the subject's display text. It shows no field value.
  taskInbox: 'none',
  // The inspector lists process instances, with their state and step. A state is not an enum field value.
  processInspector: 'none',
}

const valueLabelKeys = {
  draft: 'request.status.draft',
  submitted: 'request.status.submitted',
  approved: 'request.status.approved',
}
const labels = ['Bản nháp', 'Đã gửi', 'Đã duyệt']
const texts: Record<string, string> = {
  'request.status': 'Trạng thái',
  'request.status.draft': 'Bản nháp',
  'request.status.submitted': 'Đã gửi',
  'request.status.approved': 'Đã duyệt',
}
const catalogs = [{ texts, fallbackTexts: texts }]

const status: FieldMetadata = { ...fields.enum, valueLabelKeys }

function withStatus(widget: EntityWidgetMetadata): EntityWidgetMetadata {
  const entity = widget.entity!
  return { ...widget, entity: { ...entity, fields: entity.fields.map((f) => (f.name === 'status' ? status : f)) } }
}

function chart(): WidgetMetadata {
  const widget = chartWidget('bar')
  const dataSource = widget.dataSource!
  return {
    ...widget,
    dataSource: {
      ...dataSource,
      columns: dataSource.columns.map((column) => (column.name === 'status' ? { ...column, valueLabelKeys } : column)),
    },
  }
}

const lowestStatusPath = '/api/apps/DesignSystemSample/data-sources/LowestStatus/rows'

function kpi(): WidgetMetadata {
  return {
    ...kpiWidget,
    dataSource: {
      ...kpiWidget.dataSource!,
      name: 'LowestStatus',
      rowsPath: lowestStatusPath,
      pageSize: 1,
      columns: [
        {
          name: 'lowestStatus',
          type: 'enum',
          labelKey: null,
          values: Object.keys(valueLabelKeys),
          valueLabelKeys,
          target: null,
        },
      ],
    },
    kpi: { measure: 'lowestStatus', labelKey: 'request.status', comparison: null, navigate: null },
  }
}

const widgets: Record<string, () => WidgetMetadata> = {
  table: () => withStatus(tableWidget as EntityWidgetMetadata),
  form: () => withStatus(formWidget),
  detail: () => withStatus(detailWidget),
  kpi,
  chart,
}

const answers: Record<string, () => unknown> = {
  ...routes,
  [lowestStatusPath]: () => ({
    items: [{ id: requestId, version: 1, values: { lowestStatus: 'submitted' }, labels: {} }],
    page: 1,
    pageSize: 1,
    totalCount: 1,
  }),
}

beforeEach(() => {
  drawn.props.length = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string) => {
      const answer = answers[input.split('?')[0]]
      return answer ? new Response(JSON.stringify(answer()), { status: 200 }) : new Response('{}', { status: 404 })
    }),
  )
})

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
})

describe('enum labels in widgets', () => {
  it('finds the widget types', () => {
    expect(widgetTypes.length).toBeGreaterThanOrEqual(5)
  })

  it.each(widgetTypes)('classifies the %s widget', (type) => {
    expect(enumDisplay[type], `Say whether the '${type}' widget shows enum values.`).toBeDefined()
    if (enumDisplay[type] === 'labels') {
      expect(widgets[type], `Add a labelled '${type}' widget.`).toBeDefined()
    }
  })

  it.each(widgetTypes.filter((type) => enumDisplay[type] === 'labels'))(
    'shows the label of an enum value in the %s widget',
    async (type) => {
      const widget = widgets[type]()
      render(
        <MemoryRouter>
          <TextProvider catalogs={catalogs} development={false}>
            <PageLeaf
              widget={widget}
              sitePath={sitePath}
              pageName="Request"
              pages={[]}
              locale={locale}
              fallbackLocale={locale}
              pageValues={{ request: requestId }}
              recordId={requestId}
              returnTo="/"
            />
          </TextProvider>
        </MemoryRouter>,
      )

      await waitFor(() => expect(screen.getAllByText('Đã gửi').length).toBeGreaterThan(0))
      expect(screen.queryByText('submitted')).toBeNull()
      expect(screen.queryByDisplayValue('submitted')).toBeNull()
      if (type === 'form') {
        await userEvent.click(screen.getByRole('combobox', { name: /Trạng thái/ }))
        for (const label of labels) {
          expect(await screen.findByRole('option', { name: label })).toBeTruthy()
        }
      }
      if (type === 'chart') {
        const props = drawn.props[drawn.props.length - 1] as ChartProps
        expect(props.categories).toEqual(labels)
      }
    },
  )
})

describe('a data source filter over an enum parameter', () => {
  it('offers and shows the labels', async () => {
    const parameter: DataSourceParameter = {
      name: 'status',
      type: 'enum',
      required: false,
      labelKey: 'request.status',
      values: Object.keys(valueLabelKeys),
      valueLabelKeys,
      target: null,
    }
    const onChange = vi.fn()
    render(
      <TextProvider catalogs={catalogs} development={false}>
        <FilterBar
          parameters={[parameter]}
          values={{ status: 'approved' }}
          errors={{}}
          onChange={onChange}
          locale="vi"
        />
      </TextProvider>,
    )

    const select = screen.getByRole('combobox', { name: /Trạng thái/ })
    const bar = within(screen.getByTestId('filter-bar'))
    expect(bar.getByText('Đã duyệt')).toBeTruthy()
    expect(bar.queryByText('approved')).toBeNull()

    await userEvent.click(select)
    for (const label of labels) {
      expect(await screen.findByRole('option', { name: label })).toBeTruthy()
    }
    expect(screen.queryByRole('option', { name: 'draft' })).toBeNull()

    await userEvent.click(screen.getByRole('option', { name: 'Đã gửi' }))
    expect(onChange).toHaveBeenCalledWith('status', 'submitted')
  })
})
