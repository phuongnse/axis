import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { RecordHistory } from './RecordHistory'
import type { HistoryItem } from './records'
import type { FieldMetadata, FieldType, PageMetadata } from './site'
import { TextProvider } from './TextProvider'

function field(name: string, type: FieldType, labelKey: string | null): FieldMetadata {
  return {
    name,
    type,
    labelKey,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    values: null,
    target: null,
    fields: null,
  }
}

const recordsPath = '/api/apps/StepApp/entities/Request/records'
const recordId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const fields = [field('title', 'text', 'request.title'), field('comment', 'text', 'request.comment')]

const texts: Record<string, string> = {
  'shell.history.title': 'History',
  'shell.history.empty': 'No history yet.',
  'shell.history.loadFailed': 'The history could not be loaded.',
  'shell.history.system': 'System',
  'shell.history.anonymous': 'Anonymous',
  'shell.history.fields': 'Changed',
  'shell.history.outcome': 'Outcome',
  'shell.history.step': 'Step',
  'shell.history.openInstance': 'Open instance',
  'shell.history.action.record.created': 'Created',
  'shell.history.action.record.updated': 'Updated',
  'shell.history.action.process.started': 'Process started',
  'shell.history.action.task.created': 'Task created',
  'shell.history.action.task.completed': 'Task decided',
  'request.title': 'Title',
  'request.comment': 'Comment',
}
const catalogs = [{ texts, fallbackTexts: texts }]

let nextId = 0

function item(action: string, overrides: Partial<HistoryItem> = {}): HistoryItem {
  nextId += 1
  return {
    id: `01926b3e-5f40-7c8a-9b1d-${String(nextId).padStart(12, '0')}`,
    occurredAt: '2026-10-06T02:05:00.123456Z',
    actor: 'maria',
    actorName: 'Maria Manager',
    action,
    processInstanceId: null,
    details: {},
    inspectPath: null,
    ...overrides,
  }
}

function page(items: HistoryItem[], number = 1, totalCount = items.length) {
  return JSON.stringify({ items, page: number, pageSize: 20, totalCount })
}

/** Stubs `fetch` with answers in order, and returns the URLs requested. */
function stubFetch(...answers: { status: number; body: string }[]) {
  const fetchMock = vi.fn(async (_input: string) => {
    const answer = answers.shift() ?? { status: 500, body: '{}' }
    return new Response(answer.body, { status: answer.status })
  })
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

const navigation = ['Requests', 'Approvals', 'Audit']

function renderHistory() {
  return render(
    <MemoryRouter>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          <RecordHistory
            sitePath="steps"
            pages={navigation}
            recordsPath={recordsPath}
            recordId={recordId}
            fields={fields}
            locale="en"
          />
        </TextProvider>
      </ConfigProvider>
    </MemoryRouter>,
  )
}

const instanceId = '0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01'

function inspectPath(process: string) {
  return `/api/apps/StepApp/processes/${process}/instances/${instanceId}`
}

/**
 * The metadata of a page with one widget: an inspector of `process`, or a table when it is `null`.
 * The widget is inside a section when `nested` is true.
 */
function pageBody(name: string, process: string | null, nested = false) {
  const widget = {
    type: process === null ? 'table' : 'processInspector',
    entity: null,
    dataSource: null,
    form: null,
    tasksPath: null,
    process,
    instancesPath: process === null ? null : `/api/apps/StepApp/processes/${process}/instances`,
    actions: [],
    rowActions: [],
    toolbarActions: [],
    access: null,
    detail: null,
    kpi: null,
    chart: null,
  }
  const widgets = nested
    ? [{ type: 'section', titleKey: 'section.title', collapsed: false, widgets: [widget] }]
    : [widget]
  return JSON.stringify({ name, titleKey: `${name}.title`, widgets } as unknown as PageMetadata)
}

/** Stubs `fetch` by URL: the history answers `history`, and a page answers its metadata or a 404. */
function stubRoutes(history: string, pages: Record<string, string | null>) {
  const fetchMock = vi.fn(async (input: string) => {
    if (input.includes('/history?')) {
      return new Response(history, { status: 200 })
    }
    const name = decodeURIComponent(input.split('/pages/')[1])
    const body = pages[name]
    return body ? new Response(body, { status: 200 }) : new Response('{}', { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

describe('RecordHistory', () => {
  it('lists the entries in the order the server gives, newest first, with their action, actor and time', async () => {
    const requests = stubFetch({
      status: 200,
      body: page([
        item('task.completed', {
          occurredAt: '2026-10-06T02:10:00Z',
          details: { outcome: 'approve', fields: ['comment'] },
        }),
        item('task.created', { actor: 'system', actorName: null, details: { step: 'decide', taskId: 'x' } }),
        item('record.updated', { actor: 'gone', actorName: null, details: { version: 2, fields: ['title'] } }),
        item('record.created', { actor: 'anonymous', actorName: null, details: { version: 1 } }),
      ]),
    })

    renderHistory()

    const entries = await screen.findAllByTestId('history-entry')
    expect(entries.map((entry) => entry.querySelector('strong')?.textContent)).toEqual([
      'Task decided',
      'Task created',
      'Updated',
      'Created',
    ])
    expect(entries[0]).toHaveTextContent('Maria Manager')
    expect(entries[0]).toHaveTextContent('Changed: Comment · Outcome: approve')
    expect(entries[0].querySelector('time')).toHaveAttribute('datetime', '2026-10-06T02:10:00Z')
    expect(entries[0].querySelector('time')?.textContent).toBe(
      new Intl.DateTimeFormat('en', { dateStyle: 'medium', timeStyle: 'medium' }).format(
        new Date('2026-10-06T02:10:00Z'),
      ),
    )
    expect(entries[1]).toHaveTextContent('System')
    expect(entries[1]).toHaveTextContent('Step: decide')
    expect(entries[2]).toHaveTextContent('gone')
    expect(entries[3]).toHaveTextContent('Anonymous')
    expect(requests()).toEqual([`${recordsPath}/${recordId}/history?page=1&pageSize=20`])
  })

  it('shows an action without a text as it is', async () => {
    stubFetch({ status: 200, body: page([item('record.archived')]) })

    renderHistory()

    expect(await screen.findByTestId('history-entry')).toHaveTextContent('record.archived')
  })

  it('shows the empty text when the record has no history', async () => {
    stubFetch({ status: 200, body: page([]) })

    renderHistory()

    expect(await screen.findByText('No history yet.')).toBeInTheDocument()
  })

  it('shows an error inside the panel when the history fails to load', async () => {
    stubFetch({ status: 500, body: '{}' })

    renderHistory()

    expect(await screen.findByTestId('record-history-error')).toHaveTextContent('The history could not be loaded.')
    expect(screen.getByTestId('record-history')).toHaveTextContent('History')
  })

  it('pages through more than twenty entries', async () => {
    const first = Array.from({ length: 20 }, () => item('record.updated'))
    const requests = stubFetch(
      { status: 200, body: page(first, 1, 21) },
      { status: 200, body: page([item('record.created')], 2, 21) },
    )

    renderHistory()

    expect(await screen.findAllByTestId('history-entry')).toHaveLength(20)
    await userEvent.click(screen.getByTitle('2'))

    expect(await screen.findByText('Created')).toBeInTheDocument()
    expect(screen.getAllByTestId('history-entry')).toHaveLength(1)
    expect(requests()).toEqual([
      `${recordsPath}/${recordId}/history?page=1&pageSize=20`,
      `${recordsPath}/${recordId}/history?page=2&pageSize=20`,
    ])
  })

  it('is busy while a page loads, and marks the current page', async () => {
    const first = Array.from({ length: 20 }, () => item('record.updated'))
    stubFetch({ status: 200, body: page(first, 1, 21) }, { status: 200, body: page([item('record.created')], 2, 21) })

    renderHistory()
    const panel = screen.getByTestId('record-history')
    expect(panel).toHaveAttribute('aria-busy', 'true')

    await screen.findAllByTestId('history-entry')
    expect(panel).toHaveAttribute('aria-busy', 'false')
    expect(panel.querySelector('[aria-current="page"]')).toHaveTextContent('1')

    await userEvent.click(screen.getByTitle('2'))

    await screen.findByText('Created')
    expect(panel).toHaveAttribute('aria-busy', 'false')
    expect(panel.querySelector('[aria-current="page"]')).toHaveTextContent('2')
  })

  it('shows no paging control for twenty entries or fewer', async () => {
    stubFetch({ status: 200, body: page([item('record.created')]) })

    renderHistory()

    await screen.findByTestId('history-entry')
    expect(screen.queryByTitle('1')).toBeNull()
  })

  it('links an entry to the instance on the first navigation page that inspects its process', async () => {
    const requests = stubRoutes(
      page([item('record.updated', { processInstanceId: instanceId, inspectPath: inspectPath('Submit') })]),
      {
        Requests: pageBody('Requests', null),
        Approvals: pageBody('Approvals', 'Submit'),
        Audit: pageBody('Audit', 'Submit'),
      },
    )

    renderHistory()

    const link = await screen.findByTestId('history-inspect-link')
    expect(link).toHaveTextContent('Open instance')
    expect(link).toHaveAttribute('href', `/steps/approvals/${instanceId}`)
    expect(requests()).toContain('/api/sites/steps/pages/Requests')
    expect(requests()).toContain('/api/sites/steps/pages/Approvals')
    expect(requests()).toContain('/api/sites/steps/pages/Audit')
  })

  it('links an entry to an inspector nested in a container on a navigation page', async () => {
    stubRoutes(page([item('record.updated', { processInstanceId: instanceId, inspectPath: inspectPath('Submit') })]), {
      Requests: pageBody('Requests', null),
      Approvals: pageBody('Approvals', 'Submit', true),
      Audit: null,
    })

    renderHistory()

    const link = await screen.findByTestId('history-inspect-link')
    expect(link).toHaveAttribute('href', `/steps/approvals/${instanceId}`)
  })

  it('shows no link when no navigation page inspects the entry’s process', async () => {
    const requests = stubRoutes(
      page([item('record.updated', { processInstanceId: instanceId, inspectPath: inspectPath('Submit') })]),
      {
        Requests: pageBody('Requests', null),
        Approvals: pageBody('Approvals', 'Decide'),
        Audit: null,
      },
    )

    renderHistory()

    await screen.findByTestId('history-entry')
    await waitFor(() => expect(requests().filter((url) => String(url).includes('/pages/'))).toHaveLength(3))
    expect(screen.queryByTestId('history-inspect-link')).toBeNull()
  })

  it('shows no link and loads no page for an entry without an inspect path', async () => {
    const requests = stubRoutes(page([item('record.updated', { processInstanceId: instanceId })]), {
      Approvals: pageBody('Approvals', 'Submit'),
    })

    renderHistory()

    await screen.findByTestId('history-entry')
    expect(screen.queryByTestId('history-inspect-link')).toBeNull()
    expect(requests().filter((url) => String(url).includes('/pages/'))).toEqual([])
  })

  it('links only the entry that has an inspect path', async () => {
    stubRoutes(
      page([
        item('record.updated', { processInstanceId: instanceId, inspectPath: inspectPath('Submit') }),
        item('record.created'),
      ]),
      { Approvals: pageBody('Approvals', 'Submit') },
    )

    renderHistory()

    await screen.findByTestId('history-inspect-link')
    const entries = screen.getAllByTestId('history-entry')
    expect(entries[0].querySelector('[data-testid="history-inspect-link"]')).not.toBeNull()
    expect(entries[1].querySelector('[data-testid="history-inspect-link"]')).toBeNull()
  })
})
