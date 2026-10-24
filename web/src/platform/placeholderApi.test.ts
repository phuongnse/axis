import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  chartWidget,
  entity,
  formWidget,
  kpiWidget,
  processInspectorWidget,
  tableWidget,
  taskInboxWidget,
} from '../designSystem/fixtures'
import { apiFetch, setApiGuard } from './api'
import { fetchFormState } from './formState'
import { fetchInstances } from './inspection'
import { placeholderApi, placeholderPageValues } from './placeholderApi'
import { fetchHistory, fetchRecord, fetchRecords } from './records'
import type { PageMetadata, PageParameter } from './site'
import { fetchTasks } from './tasks'

const page: PageMetadata = {
  name: 'Overview',
  titleKey: 'overview.title',
  parameters: [],
  widgets: [
    {
      type: 'section',
      titleKey: 'overview.section',
      collapsed: false,
      widgets: [tableWidget, kpiWidget, chartWidget('bar'), formWidget, taskInboxWidget, processInspectorWidget],
    },
  ],
}

const recordsPath = entity.recordsPath
const categoriesPath = entity.fields.find((field) => field.type === 'reference')!.target!.recordsPath

beforeEach(() => {
  const fetchMock = vi.fn(async () => new Response(null, { status: 500 }))
  vi.stubGlobal('fetch', fetchMock)
  setApiGuard(placeholderApi(page))
})

afterEach(() => {
  setApiGuard(null)
  vi.unstubAllGlobals()
})

describe('placeholderApi', () => {
  it('answers a records request with three rows whose values match each field type', async () => {
    const records = await fetchRecords(recordsPath, 'page=2&pageSize=5')

    expect([records.page, records.pageSize, records.totalCount, records.items.length]).toEqual([2, 5, 3, 3])
    expect(records.items[1]).toEqual({
      id: '00000000-0000-4000-8000-000000000002',
      version: 1,
      values: {
        title: 'Sample title 2',
        quantity: '20',
        amount: '2501.00',
        urgent: false,
        neededOn: '2026-03-02',
        submittedAt: '2026-03-02T09:30:00.000000Z',
        status: 'submitted',
        category: '00000000-0000-4000-8000-000000000101',
        lines: [
          { description: 'Sample description 1', count: '10' },
          { description: 'Sample description 2', count: '20' },
        ],
      },
      labels: { category: 'Sample Category' },
    })
    expect(records.items[0].values.urgent).toBe(true)
    expect(records.items[2].values.status).toBe('approved')
  })

  it('defaults the page to 1 and the page size to 20', async () => {
    const records = await fetchRecords(recordsPath, '')

    expect([records.page, records.pageSize]).toEqual([1, 20])
  })

  it('answers the same values on every request', async () => {
    expect(await fetchRecords(recordsPath, '')).toEqual(await fetchRecords(recordsPath, ''))
  })

  it('answers one record with the requested id, and its history empty', async () => {
    const id = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'

    const record = await fetchRecord(recordsPath, id)
    const history = await fetchHistory(recordsPath, id, 1)

    expect(record?.id).toBe(id)
    expect(record?.values.title).toBe('Sample title 1')
    expect(history).toEqual({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  })

  it('answers the records of a reference target with the display field only', async () => {
    const targets = await fetchRecords(categoriesPath, '')

    expect(targets.items.map((item) => item.values)).toEqual([
      { name: 'Sample Category 1' },
      { name: 'Sample Category 2' },
      { name: 'Sample Category 3' },
    ])
  })

  it('answers one total row for a kpi', async () => {
    const rows = await fetchRecords(kpiWidget.dataSource!.rowsPath, '')

    expect(rows.items).toHaveLength(1)
    expect(rows.items[0].values).toEqual({ requests: '10', amountSum: '1250.50' })
  })

  it('answers several groups for a chart, taking the values of an enum category', async () => {
    const rows = await fetchRecords(chartWidget('bar').dataSource!.rowsPath, '')

    expect(rows.items.map((item) => item.values.status)).toEqual(['draft', 'submitted', 'approved'])
  })

  it('lists every section and field of a form as visible', async () => {
    const state = await fetchFormState(`/api/apps/DesignSystemSample/forms/${formWidget.form!.name}/state`, '{}')

    expect(state.sections).toEqual([
      { name: 'general', visible: true },
      { name: 'details', visible: true },
    ])
    expect(Object.keys(state.fields)).toEqual(entity.fields.map((field) => field.name))
    expect(Object.values(state.fields).every((field) => field.visible && !field.readOnly)).toBe(true)
    expect(state.errors).toEqual({})
  })

  it('answers empty pages for tasks and process instances', async () => {
    const tasks = await fetchTasks(taskInboxWidget.tasksPath!, '')
    const instances = await fetchInstances(processInspectorWidget.instancesPath!, '')

    expect(tasks).toEqual({ items: [], page: 1, pageSize: 20, totalCount: 0 })
    expect(instances).toEqual({ items: [], page: 1, pageSize: 20, totalCount: 0 })
  })

  it('answers a write to a records path with a local 404 and a problem body', async () => {
    const response = await apiFetch(recordsPath, { method: 'POST', body: '{}' })

    expect(response.status).toBe(404)
    expect(response.headers.get('Content-Type')).toBe('application/problem+json')
    expect(await response.json()).toEqual({
      status: 404,
      title: 'The page designer does not send this request.',
      errors: {},
    })
  })

  it('answers a delete and an unknown /api/apps/ path with a local 404', async () => {
    const deleted = await apiFetch(`${recordsPath}/6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7`, { method: 'DELETE' })
    const unknown = await apiFetch('/api/apps/DesignSystemSample/entities/Other/records')

    expect([deleted.status, unknown.status]).toEqual([404, 404])
  })

  it('never sends a request to the server', async () => {
    await fetchRecords(recordsPath, '')
    await apiFetch('/api/apps/DesignSystemSample/unknown', { method: 'POST' })

    expect(fetch).not.toHaveBeenCalled()
  })
})

describe('placeholderPageValues', () => {
  const parameter = (name: string, type: PageParameter['type'], values: string[] | null = null): PageParameter => ({
    name,
    type,
    required: true,
    values,
    target: null,
  })

  it('makes a string for each parameter from its type, and a placeholder record id for a reference', () => {
    expect(
      placeholderPageValues([
        parameter('title', 'text'),
        parameter('count', 'integer'),
        parameter('since', 'date'),
        parameter('state', 'enum', ['open', 'closed']),
        parameter('ticket', 'reference'),
      ]),
    ).toEqual({
      title: 'Sample title 1',
      count: '10',
      since: '2026-03-01',
      state: 'open',
      ticket: '00000000-0000-4000-8000-000000000101',
    })
  })

  it('is empty for a page with no parameters', () => {
    expect(placeholderPageValues([])).toEqual({})
  })
})
