import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import { describe, expect, it, vi } from 'vitest'
import { RecordHistory } from './RecordHistory'
import type { HistoryItem } from './records'
import type { FieldMetadata, FieldType } from './site'
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

function renderHistory() {
  return render(
    <ConfigProvider theme={{ token: { motion: false } }}>
      <TextProvider catalogs={catalogs} development={false}>
        <RecordHistory recordsPath={recordsPath} recordId={recordId} fields={fields} locale="en" />
      </TextProvider>
    </ConfigProvider>,
  )
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

  it('shows no paging control for twenty entries or fewer', async () => {
    stubFetch({ status: 200, body: page([item('record.created')]) })

    renderHistory()

    await screen.findByTestId('history-entry')
    expect(screen.queryByTitle('1')).toBeNull()
  })
})
