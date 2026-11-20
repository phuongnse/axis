import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { formItem, optionTexts } from '../test/antd'
import { dialog } from '../test/tableFilter'
import { isSignInNeeded } from './signInNeeded'
import { assignLocation } from './navigation'
import { ProcessInstancePage } from './ProcessInstancePage'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

vi.mock('./navigation')

const instancesPath = '/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances'
const instanceId = '0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b'
const releaseId = '0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01'

const inspectorWidget: WidgetMetadata = {
  type: 'processInspector',
  entity: null,
  dataSource: null,
  form: null,
  tasksPath: null,
  process: 'PurchaseRequestApproval',
  instancesPath,
  deadLettersPath: '/api/apps/PurchasingApp/processes/PurchaseRequestApproval/dead-letters',
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  priorities: {},
  access: null,
  detail: null,
  kpi: null,
  chart: null,
}

// The instance detail example of the processes reference, with an open task and two calls. The
// source text of the visible input keeps its decimal digits.
const detailText = `{
  "id": "${instanceId}",
  "process": "PurchaseRequestApproval",
  "processTitleKey": "processes.purchaseRequestApproval.title",
  "subjectId": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
  "subjectEntity": "request",
  "subjectLabel": "PR-1001",
  "releaseId": "${releaseId}",
  "releaseNumber": 3,
  "state": "waiting",
  "step": "registerPurchase",
  "createdAt": "2026-10-10T02:05:00.123456Z",
  "endedAt": null,
  "startedBy": { "kind": "person", "name": "Ada Lovelace", "id": "ada", "via": "purchaseRequest.submit" },
  "input": null,
  "result": null,
  "studio": null,
  "graph": {
    "start": "managerApproval",
    "steps": [
      {
        "name": "managerApproval",
        "type": "task",
        "labelKey": "purchaseRequest.managerApproval",
        "event": null,
        "eventLabelKey": null,
        "transitions": [
          { "next": "registerPurchase", "on": "approve", "branch": null, "labelKey": "task.approve", "after": null },
          { "next": "done", "on": "reject", "branch": null, "labelKey": null, "after": null }
        ]
      },
      {
        "name": "registerPurchase",
        "type": "operation",
        "labelKey": null,
        "event": null,
        "eventLabelKey": null,
        "transitions": [{ "next": "done", "on": null, "branch": null, "labelKey": null, "after": null }]
      },
      { "name": "done", "type": "end", "labelKey": null, "event": null, "eventLabelKey": null, "transitions": [] }
    ]
  },
  "path": [
    {
      "step": "managerApproval",
      "startedAt": "2026-10-10T02:05:00.123456Z",
      "finishedAt": "2026-10-10T02:05:00.130000Z",
      "decision": null,
      "output": { "state": "waiting", "taskId": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01" },
      "error": null,
      "problem": null,
      "traceId": "4bf92f3577b34da6a3ce929d0e0e4736"
    }
  ],
  "tasks": [
    {
      "id": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01",
      "step": "managerApproval",
      "assigneeKind": "role",
      "assignee": "manager",
      "state": "open",
      "outcome": null,
      "completedBy": null,
      "createdAt": "2026-10-10T02:05:00.123456Z",
      "completedAt": null
    }
  ],
  "calls": [
    {
      "id": "0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c",
      "kind": "connector",
      "step": "registerPurchase",
      "connector": "PurchasingSystem",
      "operation": "createOrder",
      "state": "pending",
      "attempts": [
        {
          "kind": "attempt",
          "number": 1,
          "startedAt": "2026-10-10T04:00:01.000000Z",
          "finishedAt": "2026-10-10T04:00:03.000000Z",
          "outcome": "retryable",
          "httpStatus": 503,
          "error": null,
          "problem": null,
          "traceId": "0af7651916cd43dd8448eb211c80319c"
        }
      ],
      "input": null,
      "inputHidden": true,
      "resolvable": false
    },
    {
      "id": "0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8d",
      "kind": "package",
      "step": "registerPurchase",
      "package": "PaymentReferences",
      "version": "1.0.0",
      "idempotent": false,
      "operation": "reference",
      "state": "succeeded",
      "attempts": [],
      "input": { "title": "Laptops", "amount": 1250.50, "lines": ["a", "b"] },
      "inputHidden": false,
      "resolvable": false
    }
  ],
  "parent": null,
  "subProcesses": [],
  "commands": []
}`

const texts = {
  ...platformEnglish,
  'purchaseRequest.managerApproval': 'Manager approval',
  'purchaseRequest.submit': 'Submit for approval',
  'task.approve': 'Approve',
  'order.again': 'Go again',
}

/** The texts of the instance's release, which give the process its title. */
const releaseTextsBody = JSON.stringify({
  locale: 'en',
  texts: {
    'processes.purchaseRequestApproval.title': 'Purchase request approval',
    'purchaseRequest.submit': 'Submit for approval',
  },
})
const catalogs = [{ texts, fallbackTexts: texts }]

interface Answer {
  status: number
  body?: string
}

/** The detail text with other `commands`, and with its package call and its connector call failed. */
function detailWith(commands: string[], failed?: 'package' | 'connector') {
  const detail = JSON.parse(detailText) as {
    commands: string[]
    state: string
    calls: { kind: string; state: string }[]
  }
  detail.commands = commands
  if (failed) {
    detail.state = 'failed'
    for (const call of detail.calls) {
      call.state = call.kind === failed ? 'failed' : 'succeeded'
    }
  }
  return JSON.stringify(detail)
}

/** The detail text as a failed instance at `registerPurchase`, whose entry for that step holds `problem` and `error`. */
function detailFailedAt(problem: { code: string; values: Record<string, number> } | null, error: string | null = null) {
  const detail = JSON.parse(detailText) as { state: string; path: Record<string, unknown>[] }
  detail.state = 'failed'
  detail.path.push({
    step: 'registerPurchase',
    startedAt: '2026-10-10T04:00:00.000000Z',
    finishedAt: '2026-10-10T04:00:03.000000Z',
    decision: null,
    output: null,
    error,
    problem,
    traceId: null,
  })
  return JSON.stringify(detail)
}

const studioResourceId = '0192f6d1-7b2e-7c40-8a9f-6e7d8c9b0a1f'

/** The detail text with the application and the resource that open the process in Studio. */
function detailWithStudio() {
  const detail = JSON.parse(detailText) as Record<string, unknown>
  return JSON.stringify({ ...detail, studio: { app: 'PurchasingApp', resource: studioResourceId } })
}

const parentId = '0192f4c0-1a2b-7c3d-8e4f-5a6b7c8d9e01'
const childId = '0192f4c0-1a2b-7c3d-8e4f-5a6b7c8d9e02'

/** A detail without a subject, waiting at the timer `wait`, with a parent and one child. */
function detailWithRelatives() {
  const detail = JSON.parse(detailText) as Record<string, unknown>
  return JSON.stringify({
    ...detail,
    subjectId: null,
    subjectEntity: null,
    subjectLabel: null,
    startedBy: { kind: 'parent', name: 'PurchaseRequestApproval', id: parentId, via: null },
    step: 'wait',
    graph: {
      start: 'wait',
      steps: [
        {
          name: 'wait',
          type: 'timer',
          labelKey: null,
          event: null,
          eventLabelKey: null,
          transitions: [{ next: 'done', on: null, branch: null, labelKey: null, after: null }],
        },
        { name: 'done', type: 'end', labelKey: null, event: null, eventLabelKey: null, transitions: [] },
      ],
    },
    path: [
      {
        step: 'wait',
        startedAt: '2026-10-10T02:05:00Z',
        finishedAt: '2026-10-10T02:05:00Z',
        decision: null,
        output: { state: 'waiting', dueAt: '2026-10-11T02:05:00Z' },
        error: null,
        problem: null,
        traceId: null,
      },
    ],
    tasks: [],
    calls: [],
    parent: {
      id: parentId,
      process: 'PurchaseRequestApproval',
      processTitleKey: 'processes.purchaseRequestApproval.title',
      step: 'runChild',
    },
    subProcesses: [
      {
        id: childId,
        process: 'ChildJob',
        processTitleKey: 'processes.childJob.title',
        step: 'wait',
        state: 'running',
        createdAt: '2026-10-10T02:05:00Z',
        endedAt: null,
      },
    ],
  })
}

/**
 * A completed instance without a subject that went again once: again, check, again, check, done, each run 2 seconds,
 * with a wait whose timeout reads After 1 hr. The fallback of `check` leads to `done`.
 */
function detailLooping() {
  const detail = JSON.parse(detailWithRelatives()) as Record<string, unknown>
  const run = (step: string, decision: string | null, second: number) => ({
    step,
    startedAt: `2026-10-10T02:05:${String(second).padStart(2, '0')}Z`,
    finishedAt: `2026-10-10T02:05:${String(second + 2).padStart(2, '0')}Z`,
    decision,
    output: null,
    error: null,
    problem: null,
    traceId: null,
  })
  const step = (name: string, type: string, transitions: Record<string, unknown>[], extra = {}) => ({
    name,
    type,
    labelKey: null,
    event: null,
    eventLabelKey: null,
    transitions,
    ...extra,
  })
  const move = (next: string, extra = {}) => ({ next, on: null, branch: null, labelKey: null, after: null, ...extra })
  return JSON.stringify({
    ...detail,
    state: 'completed',
    step: 'done',
    endedAt: '2026-10-10T02:05:09Z',
    parent: null,
    subProcesses: [],
    graph: {
      start: 'again',
      steps: [
        step('again', 'operation', [move('check')]),
        step('check', 'decision', [
          move('again', { on: '0', branch: 'repeat', labelKey: 'order.again' }),
          move('wait', { on: '1', branch: 'pause' }),
          move('done', { on: 'otherwise' }),
        ]),
        step('wait', 'waitEvent', [move('done'), move('done', { on: 'timeout', after: 'PT1H' })], {
          event: 'order.shipped',
        }),
        step('done', 'end', []),
      ],
    },
    path: [
      run('again', null, 0),
      run('check', '0', 2),
      run('again', null, 4),
      run('check', 'otherwise', 6),
      { ...run('done', null, 8), finishedAt: '2026-10-10T02:05:08.100Z' },
    ],
  })
}

/** The instance of `detailWithRelatives`, which has no subject, with `patch` laid over it. */
function detailWithData(patch: Record<string, unknown>) {
  return JSON.stringify({ ...(JSON.parse(detailWithRelatives()) as Record<string, unknown>), ...patch })
}

const startedWith = [
  { name: 'title', value: 'Laptops', hidden: false },
  { name: 'budget', value: null, hidden: true },
]

/** The metadata of a page that holds one process inspector of `process`. */
function inspectorPage(name: string, process: string) {
  return JSON.stringify({
    name,
    titleKey: `${name}.title`,
    widgets: [{ ...inspectorWidget, process, instancesPath: `/api/apps/PurchasingApp/processes/${process}/instances` }],
  })
}

const resolvableCallId = '0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c'

/** The detail text with its first call `unknown` and resolvable, with three declared outputs. */
function detailWithResolvable() {
  const detail = JSON.parse(detailText) as {
    calls: { state: string; resolvable: boolean; outputs?: { name: string; type: string; required: boolean }[] }[]
  }
  detail.calls[0].state = 'unknown'
  detail.calls[0].resolvable = true
  detail.calls[0].outputs = [
    { name: 'orderNumber', type: 'text', required: true },
    { name: 'total', type: 'decimal', required: false },
    { name: 'confirmed', type: 'boolean', required: false },
  ]
  return JSON.stringify(detail)
}

function stubApi({
  detail = { status: 200, body: detailText },
  command = { status: 200, body: '{}' },
  resolve = { status: 200, body: '{}' },
  reloaded,
  releaseTexts = { status: 200, body: releaseTextsBody },
  me = { id: 'ada', displayName: 'Ada Lovelace', roles: [], users: false },
}: {
  detail?: Answer
  command?: Answer
  resolve?: Answer
  reloaded?: Answer
  releaseTexts?: Answer
  /** The signed-in user, which `GET /api/me` answers. */
  me?: object
} = {}) {
  let reads = 0
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url === `${instancesPath}/${instanceId}`) {
      reads += 1
      const answer = reads > 1 && reloaded ? reloaded : detail
      return new Response(answer.body ?? '', { status: answer.status })
    }
    if (init?.method === 'POST' && (url.endsWith('/cancel') || url.endsWith('/retry'))) {
      return new Response(command.body ?? '', { status: command.status })
    }
    if (init?.method === 'POST' && url.endsWith('/resolve')) {
      return new Response(resolve.body ?? '', { status: resolve.status })
    }
    if (url === '/api/sites/e2e/pages/Inspector') {
      return new Response(inspectorPage('Inspector', 'PurchaseRequestApproval'), { status: 200 })
    }
    if (url === '/api/sites/e2e/pages/Children') {
      return new Response(inspectorPage('Children', 'ChildJob'), { status: 200 })
    }
    if (url === '/api/me') {
      return new Response(JSON.stringify(me), { status: 200 })
    }
    if (url === `/api/sites/e2e/texts/en?release=${releaseId}`) {
      return new Response(releaseTexts.body ?? '', { status: releaseTexts.status })
    }
    return new Response('', { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

/** The address's query, for a test that reads what the page keeps there. */
function Address() {
  return <span data-testid="address">{useLocation().search}</span>
}

function renderPage(search = '') {
  return render(
    <MemoryRouter initialEntries={[`/e2e/inspector/${instanceId}${search}`]}>
      <Address />
      <ThemeModeProvider>
        <TextProvider catalogs={catalogs} development={false}>
          <ProcessInstancePage
            sitePath="e2e"
            pages={['Inspector', 'Children']}
            widget={inspectorWidget}
            instanceId={instanceId}
            title="Approvals"
            locale="en"
            fallbackLocale="en"
          />
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

/** The page opened on its History tab, with `search` joined to the address's own, such as a table's filter. */
function renderHistory(search = '') {
  return renderPage(`?tab=history${search === '' ? '' : `&${search.slice(1)}`}`)
}

/** The address part that filters a table's `field` to `value`, under the table's `prefix`. */
function filterSearch(prefix: string, field: string, value: string) {
  const filter = encodeURIComponent(JSON.stringify({ all: [{ field, op: 'eq', value }] }))
  return `?${prefix}filter=${filter}`
}

/** The detail text with a second task, as given over the first task's own properties. */
function detailWithTasks(second: Record<string, unknown>) {
  const detail = JSON.parse(detailText) as { tasks: Record<string, unknown>[] }
  detail.tasks.push({ ...detail.tasks[0], id: '0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f02', ...second })
  return JSON.stringify(detail)
}

describe('ProcessInstancePage', () => {
  it('shows no record fact, the parent in Started by, and the sub-processes by their process title', async () => {
    stubApi({ detail: { status: 200, body: detailWithRelatives() } })

    renderHistory()

    const facts = await screen.findByTestId('studio-section-inspector-facts')
    expect(within(facts).queryByText('Record')).not.toBeInTheDocument()
    const parentLink = within(facts).getByTestId('inspector-parent-link')
    expect(parentLink).toHaveTextContent('Purchase request approval')
    expect(parentLink).toHaveAttribute('href', `/e2e/inspector/${parentId}`)
    const childLink = await screen.findByTestId(`inspector-subprocess-${childId}`)
    expect(childLink).toHaveAttribute('href', `/e2e/children/${childId}`)
    expect(within(childLink.closest('tr')!).getByText('ChildJob')).toBeInTheDocument()
  })

  it('shows the empty text and no parent for an instance without relatives', async () => {
    stubApi()

    renderHistory()

    await screen.findByTestId('inspector-instance')
    expect(screen.getByText('No sub-processes.')).toBeInTheDocument()
    expect(screen.queryByTestId('inspector-parent-link')).not.toBeInTheDocument()
  })

  it('shows the open task under History', async () => {
    stubApi()

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const taskRow = screen.getByRole('row', { name: /role: manager/ })
    expect(within(taskRow).getByText('Manager approval')).toBeInTheDocument()
    expect(within(taskRow).getByText('open')).toBeInTheDocument()
  })

  it('has the tabs Diagram, History and Data, Diagram first and open, with no count', async () => {
    stubApi()

    renderPage()

    await screen.findByTestId('inspector-instance')
    const tabs = screen.getAllByRole('tab')
    expect(tabs.map((tab) => tab.textContent)).toEqual(['Diagram', 'History', 'Data'])
    expect(tabs[0]).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByTestId('address')).toHaveTextContent('')
    expect(screen.queryByRole('region', { name: 'Tasks' })).not.toBeInTheDocument()
  })

  it('keeps tab=history in the address when History is chosen, and leaves it out for Diagram', async () => {
    stubApi()
    renderPage()
    await screen.findByTestId('inspector-instance')

    await userEvent.click(screen.getByRole('tab', { name: 'History' }))

    expect(screen.getByTestId('address')).toHaveTextContent('?tab=history')
    expect(screen.getByRole('region', { name: 'Tasks' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('tab', { name: 'Data' }))
    expect(screen.getByTestId('address')).toHaveTextContent('?tab=data')

    await userEvent.click(screen.getByRole('tab', { name: 'Diagram' }))
    expect(screen.getByTestId('address')).toHaveTextContent('')
  })

  it('shows History selected, with its history table, when the address has tab=history', async () => {
    stubApi()

    renderPage('?tab=history')

    await screen.findByTestId('inspector-instance')
    expect(screen.getByRole('tab', { name: 'History' })).toHaveAttribute('aria-selected', 'true')
    expect(screen.getByRole('tab', { name: 'Diagram' })).toHaveAttribute('aria-selected', 'false')
    expect(screen.getByRole('region', { name: 'History' })).toBeInTheDocument()
    expect(screen.queryByTestId('inspector-diagram')).not.toBeInTheDocument()
  })

  it('shows the Diagram when the tab in the address is not one of the tabs', async () => {
    stubApi()

    renderPage('?tab=nothing')

    expect(await screen.findByTestId('inspector-diagram')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: 'Diagram' })).toHaveAttribute('aria-selected', 'true')
  })

  it('draws a state badge in its tone on each step reached and none on a step not reached, with the current step chosen', async () => {
    stubApi()

    renderPage()

    const approval = await screen.findByTestId('rf__node-managerApproval', undefined, { timeout: 5000 })
    expect(within(approval).getByRole('img', { name: 'Done' })).toHaveAttribute('data-tone', 'success')
    expect(within(approval).getByText('Manager approval')).toBeInTheDocument()
    const register = screen.getByTestId('rf__node-registerPurchase')
    expect(within(register).getByRole('img', { name: 'Waiting for an event' })).toHaveAttribute('data-tone', 'neutral')
    expect(screen.getByTestId('rf__node-done').querySelector('[data-tone]')).toBeNull()
    expect(within(register).getByRole('button', { name: 'registerPurchase' })).toHaveAttribute('aria-current', 'true')
    expect(within(approval).getByRole('button', { name: 'Manager approval' })).not.toHaveAttribute('aria-current')
  })

  it('moves the chosen border to the step that is clicked', async () => {
    stubApi()
    renderPage()
    const approval = await screen.findByTestId('rf__node-managerApproval', undefined, { timeout: 5000 })

    await userEvent.click(within(approval).getByRole('button', { name: 'Manager approval' }))

    expect(within(approval).getByRole('button', { name: 'Manager approval' })).toHaveAttribute('aria-current', 'true')
    expect(
      within(screen.getByTestId('rf__node-registerPurchase')).getByRole('button', { name: 'registerPurchase' }),
    ).not.toHaveAttribute('aria-current')
  })

  it('shows the duration of the last run on a step, and the run count on a step that ran twice', async () => {
    stubApi({ detail: { status: 200, body: detailLooping() } })

    renderPage()

    const again = await screen.findByTestId('rf__node-again', undefined, { timeout: 5000 })
    expect(within(again).getByText('2 runs, last 2 sec')).toBeInTheDocument()
    expect(within(again).getByRole('img', { name: 'Done' })).toHaveAttribute('data-tone', 'success')
    expect(within(screen.getByTestId('rf__node-check')).getByText('2 runs, last 2 sec')).toBeInTheDocument()
    expect(within(screen.getByTestId('rf__node-done')).queryByText(/runs/)).not.toBeInTheDocument()
    expect(within(screen.getByTestId('rf__node-done')).getByText('< 1 sec')).toBeInTheDocument()
  })

  it('writes the author label, the branch name, Otherwise and the timeout on the transitions', async () => {
    stubApi({ detail: { status: 200, body: detailLooping() } })

    renderPage()

    await screen.findByTestId('rf__node-again', undefined, { timeout: 5000 })
    expect(await screen.findByText('Go again')).toBeInTheDocument()
    expect(screen.getByText('Otherwise')).toBeInTheDocument()
    expect(screen.getByText('After 1 hr')).toBeInTheDocument()
    expect(screen.getByText('order.shipped')).toBeInTheDocument()
    expect(screen.getByLabelText('check to done')).toBeInTheDocument()
  })

  it('shows the em dash in the completed cell of a task that is not completed', async () => {
    stubApi()

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const taskRow = screen.getByRole('row', { name: /role: manager/ })
    const headers = within(taskRow.closest('table')!).getAllByRole('columnheader')
    const completed = headers.findIndex((header) => header.textContent === 'Completed')
    expect(within(taskRow).getAllByRole('cell')[completed].textContent).toBe('—')
  })

  it('shows the history rows and the attempts of a call', async () => {
    stubApi()

    renderHistory()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByText('No history yet.')).not.toBeInTheDocument()
    const attemptRow = screen.getByRole('row', { name: /retryable/ })
    expect(within(attemptRow).getByText('503')).toBeInTheDocument()
    expect(within(attemptRow).getByText('attempt')).toBeInTheDocument()
    expect(within(attemptRow).getByText('0af76519')).toBeInTheDocument()
    const historyRow = screen.getByRole('row', { name: /^Manager approval .*4bf92f/ })
    expect(within(historyRow).getByText('4bf92f35')).toBeInTheDocument()
  })

  it('words a problem in the Error cells, with the seconds of a timeout, and shows the stored error when there is none', async () => {
    const detail = JSON.parse(detailText) as {
      path: Record<string, unknown>[]
      calls: { attempts: Record<string, unknown>[] }[]
    }
    detail.path.push({
      ...detail.path[0],
      step: 'registerPurchase',
      traceId: null,
      error: 'The operation did not finish within its timeout.',
      problem: { code: 'timeout', values: { seconds: 30 } },
    })
    detail.path.push({
      ...detail.path[0],
      step: 'done',
      traceId: null,
      error: 'System.InvalidOperationException',
      problem: null,
    })
    detail.calls[0].attempts[0].error = "The operation 'createOrder' answered with the status 503."
    detail.calls[0].attempts[0].problem = { code: 'serverError', values: {} }
    stubApi({ detail: { status: 200, body: JSON.stringify(detail) } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const timedOut = screen.getByRole('row', { name: /^registerPurchase / })
    expect(within(timedOut).getByText('No answer came within 30 sec.')).toBeInTheDocument()
    expect(within(timedOut).queryByText(/did not finish/)).not.toBeInTheDocument()
    const stored = screen.getByRole('row', { name: /^done / })
    expect(within(stored).getByText('System.InvalidOperationException')).toBeInTheDocument()
    const attempt = screen.getByRole('row', { name: /retryable/ })
    expect(within(attempt).getByText('The outside system answered with a server error.')).toBeInTheDocument()
    expect(within(attempt).queryByText(/answered with the status 503/)).not.toBeInTheDocument()
  })

  it('shows the stored error for a code this build does not know', async () => {
    const detail = JSON.parse(detailText) as { path: Record<string, unknown>[] }
    detail.path[0].error = 'A fixed text.'
    detail.path[0].problem = { code: 'somethingNew', values: {} }
    stubApi({ detail: { status: 200, body: JSON.stringify(detail) } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    expect(screen.getByText('A fixed text.')).toBeInTheDocument()
  })

  it('copies the whole trace ID from the copy button beside its first 8 characters', async () => {
    stubApi()
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    renderHistory()
    await screen.findByTestId('inspector-instance')
    const attemptRow = screen.getByRole('row', { name: /retryable/ })

    await userEvent.click(within(attemptRow).getByRole('button', { name: 'Copy' }))

    expect(writeText).toHaveBeenCalledExactlyOnceWith('0af7651916cd43dd8448eb211c80319c')
  })

  it('shows the em dash in the HTTP status cell of an attempt without one', async () => {
    const detail = JSON.parse(detailText) as { calls: { attempts: { httpStatus: number | null }[] }[] }
    detail.calls[0].attempts[0].httpStatus = null
    stubApi({ detail: { status: 200, body: JSON.stringify(detail) } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const attemptRow = screen.getByRole('row', { name: /retryable/ })
    const headers = within(attemptRow.closest('table')!).getAllByRole('columnheader')
    const httpStatus = headers.findIndex((header) => header.textContent === 'HTTP status')
    expect(within(attemptRow).getAllByRole('cell')[httpStatus].textContent).toBe('—')
  })

  it('shows the hidden text and no input value for a call whose input is hidden', async () => {
    stubApi()

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const hidden = screen.getByTestId('inspector-call-0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c')
    expect(within(hidden).getByText('Input: Hidden')).toBeInTheDocument()
    expect(within(hidden).getByText('PurchasingSystem')).toBeInTheDocument()
    expect(within(hidden).getByText('createOrder')).toBeInTheDocument()
    expect(within(hidden).queryByText('title')).not.toBeInTheDocument()
  })

  it('shows a call kind as plain text, and its state as a tag in words with the tone of its meaning', async () => {
    stubApi()

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const hidden = within(screen.getByTestId('inspector-call-0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c'))
    expect(hidden.getByText('connector').closest('.ant-tag')).toBeNull()
    expect(hidden.getByText('Pending').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
    const visible = within(screen.getByTestId('inspector-call-0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8d'))
    expect(visible.getByText('package').closest('.ant-tag')).toBeNull()
    expect(visible.getByText('Succeeded').closest('[data-tone]')).toHaveAttribute('data-tone', 'success')
  })

  it('names the tone of each call state, and shows a state it does not know as sent', async () => {
    const detail = JSON.parse(detailText) as { calls: { state: string }[] }
    detail.calls[0].state = 'unknown'
    detail.calls[1].state = 'archived'
    stubApi({ detail: { status: 200, body: JSON.stringify(detail) } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const first = within(screen.getByTestId('inspector-call-0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c'))
    expect(first.getByText('Outcome unknown').closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
    const second = within(screen.getByTestId('inspector-call-0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8d'))
    expect(second.getByText('archived').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
  })

  it('shows the top-level input properties of a visible input, with numbers as their source digits', async () => {
    stubApi()

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const visible = screen.getByTestId('inspector-call-0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8d')
    expect(within(visible).getByText('PaymentReferences@1.0.0')).toBeInTheDocument()
    expect(within(visible).getByText('reference')).toBeInTheDocument()
    expect(within(visible).getByText('No attempts yet.')).toBeInTheDocument()
    expect(within(visible).queryByText('Hidden')).not.toBeInTheDocument()
    expect(within(visible).getByText('Laptops')).toBeInTheDocument()
    expect(within(visible).getByText('1250.50')).toBeInTheDocument()
    expect(within(visible).getByText('["a","b"]')).toBeInTheDocument()
  })

  it('shows a step label from the texts of the instance release, and the name when there is none', async () => {
    stubApi({
      releaseTexts: {
        status: 200,
        body: JSON.stringify({ locale: 'en', texts: { 'purchaseRequest.managerApproval': 'Approval by the manager' } }),
      },
    })

    renderPage()

    const approval = await screen.findByTestId('rf__node-managerApproval', undefined, { timeout: 5000 })
    expect(within(approval).getByText('Approval by the manager')).toBeInTheDocument()
    expect(within(screen.getByTestId('rf__node-done')).getByText('done')).toBeInTheDocument()
  })

  it('keeps the heading and shows the not-found line with no command on a 404', async () => {
    stubApi({ detail: { status: 404 } })

    renderPage()

    const line = await screen.findByTestId('record-not-found')
    expect(line).toHaveTextContent('This process instance does not exist, or you cannot open it.')
    expect(screen.getByRole('heading', { level: 1, name: 'Approvals' })).toBeVisible()
    expect(within(screen.getByTestId('page-actions')).queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByTestId('inspector-instance')).not.toBeInTheDocument()
  })

  it('shows the may-not-inspect message on a 403', async () => {
    stubApi({ detail: { status: 403 } })

    renderPage()

    expect(await screen.findByTestId('inspector-forbidden')).toHaveTextContent('You may not inspect this process.')
  })

  it('shows a load error on any other failure, and when the release texts fail to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubApi({ detail: { status: 500 } })
    const { unmount } = renderPage()

    expect(await screen.findByTestId('inspector-error')).toBeInTheDocument()
    unmount()

    stubApi({ releaseTexts: { status: 500 } })
    renderPage()

    expect(await screen.findByTestId('inspector-error')).toBeInTheDocument()
    expect(screen.queryByTestId('inspector-instance')).not.toBeInTheDocument()
  })

  it('asks to sign in when nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    stubApi({ detail: { status: 401 } })

    renderPage()

    await waitFor(() => expect(isSignInNeeded()).toBe(true))
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('shows each History section as a part, with the commands on the heading', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['cancel']) } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    expect(within(screen.getByTestId('page-actions')).getByTestId('inspector-command-cancel')).toBeInTheDocument()
    for (const title of ['History', 'Tasks', 'Calls', 'Sub-processes']) {
      expect(screen.getByRole('region', { name: title })).toBeInTheDocument()
    }
    expect(screen.queryByRole('region', { name: 'Details' })).not.toBeInTheDocument()
  })

  it('is headed by the process title from the release texts, with the state as its tag and Process as its line', async () => {
    stubApi()

    renderPage()

    const heading = within(await screen.findByTestId('page-heading'))
    expect(await heading.findByRole('heading', { level: 1, name: 'Purchase request approval' })).toBeVisible()
    expect(heading.getByText('Waiting').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
    expect(heading.getByText('Process')).toBeVisible()
  })

  it('shows Open in Studio on the heading only when the detail names a studio', async () => {
    stubApi({ detail: { status: 200, body: detailWithStudio() } })
    const { unmount } = renderPage()

    const open = await screen.findByTestId('inspector-open-studio')
    expect(open).toHaveTextContent('Open in Studio')
    expect(open).toHaveAttribute('href', `/studio?app=PurchasingApp&resource=${studioResourceId}`)
    expect(within(screen.getByTestId('page-actions')).getByTestId('inspector-open-studio')).toBeInTheDocument()
    unmount()

    stubApi()
    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByTestId('inspector-open-studio')).not.toBeInTheDocument()
  })

  it('shows a failure banner under the heading that names the step and the problem, and none while waiting', async () => {
    stubApi({ detail: { status: 200, body: detailFailedAt({ code: 'serverError', values: {} }) } })
    const { unmount } = renderPage()

    const banner = await screen.findByRole('alert')
    expect(banner).toHaveTextContent('Stopped at registerPurchase. The outside system answered with a server error.')
    unmount()

    stubApi()
    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('words the banner from the stored error when the step has no problem code', async () => {
    stubApi({ detail: { status: 200, body: detailFailedAt(null, 'System.InvalidOperationException') } })

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Stopped at registerPurchase. System.InvalidOperationException',
    )
  })

  it('names the person who started the instance, with "(you)" for the signed-in person', async () => {
    stubApi()

    renderPage()

    const facts = await screen.findByTestId('studio-section-inspector-facts')
    expect(within(facts).getByText('Started by')).toBeVisible()
    // The signed-in person's name follows `GET /api/me`, which answers after the page.
    await waitFor(() => expect(within(facts).getByText('Ada Lovelace')).toHaveTextContent('Ada Lovelace (you)'))
  })

  it('shows the first 8 characters of the instance ID with a copy button that copies all of it', async () => {
    stubApi()
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    renderPage()

    const facts = await screen.findByTestId('studio-section-inspector-facts')
    expect(within(facts).getByText(instanceId.slice(0, 8))).toBeVisible()

    await userEvent.click(within(facts).getByRole('button', { name: 'Copy' }))

    expect(writeText).toHaveBeenCalledExactlyOnceWith(instanceId)
  })

  it('shows only the command buttons the detail allows', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['cancel']) } })
    const { unmount } = renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.getByTestId('inspector-command-cancel')).toHaveTextContent('Cancel instance')
    expect(screen.queryByTestId('inspector-command-retry')).not.toBeInTheDocument()
    unmount()

    stubApi({ detail: { status: 200, body: detailWith([]) } })
    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByTestId('inspector-command-cancel')).not.toBeInTheDocument()
    expect(screen.queryByTestId('inspector-command-retry')).not.toBeInTheDocument()
  })

  it('leaves out a command this build does not know', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['resolve']) } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByTestId(/^inspector-command-/)).not.toBeInTheDocument()
  })

  it('warns before a retry of a failed non-idempotent package call', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['retry'], 'package') } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-retry'))

    expect(await screen.findByTestId('inspector-retry-warning')).toBeInTheDocument()
  })

  it('shows no retry warning for a failed connector call, or for the cancel dialog', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['retry', 'cancel'], 'connector') } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-retry'))
    await screen.findByRole('dialog')
    expect(screen.queryByTestId('inspector-retry-warning')).not.toBeInTheDocument()
  })

  it('posts the reason of a cancel and shows the instance read again', async () => {
    const cancelled = JSON.parse(detailWith([])) as { state: string }
    cancelled.state = 'cancelled'
    const fetchMock = stubApi({
      detail: { status: 200, body: detailWith(['cancel']) },
      reloaded: { status: 200, body: JSON.stringify(cancelled) },
    })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Close')).toBeInTheDocument()
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), {
      target: { value: 'Requested by finance' },
    })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByTestId('inspector-command-cancel')).not.toBeInTheDocument())
    expect(within(screen.getByTestId('page-heading')).getByText('Cancelled')).toBeInTheDocument()
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(post[0]).toBe(`${instancesPath}/${instanceId}/cancel`)
    expect(JSON.parse(post[1]!.body as string)).toEqual({ reason: 'Requested by finance' })
  })

  it('gives the reason of a command its help', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['cancel']) } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('textbox', { name: 'Reason' })).toHaveAccessibleDescription(
      platformEnglish['shell.inspector.commands.reason.help'],
    )
  })

  it('blocks a blank reason and sends nothing', async () => {
    const fetchMock = stubApi({ detail: { status: 200, body: detailWith(['cancel']) } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: '   ' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)
  })

  it('shows the problem title of a refused command in the dialog', async () => {
    stubApi({
      detail: { status: 200, body: detailWith(['cancel']) },
      command: { status: 409, body: JSON.stringify({ title: 'The instance already ended.' }) },
    })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: 'Too late' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByTestId('inspector-command-error')).toHaveTextContent(
      'The instance already ended.',
    )
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('shows a generic error when the command fails any other way', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubApi({ detail: { status: 200, body: detailWith(['cancel']) }, command: { status: 500 } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: 'Try' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByTestId('inspector-command-error')).toHaveTextContent(
      'The command could not be sent.',
    )
  })

  it('gives each resolve field its help and names the outputs as a group', async () => {
    stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('textbox', { name: 'Reason' })).toHaveAccessibleDescription(
      platformEnglish['shell.inspector.commands.reason.help'],
    )
    expect(within(dialog).getByRole('radiogroup', { name: 'Outcome' })).toHaveAccessibleDescription(
      platformEnglish['shell.inspector.commands.outcome.help'],
    )

    await userEvent.click(within(dialog).getByLabelText('Succeeded'))
    const outputs = within(dialog).getByRole('group', { name: 'Outputs' })
    expect(outputs).toHaveAccessibleDescription(platformEnglish['shell.inspector.commands.outputs.help'])
    const outputHelp = platformEnglish['shell.inspector.commands.output.help']
    expect(within(outputs).getByRole('textbox', { name: 'orderNumber (text)' })).toHaveAccessibleDescription(outputHelp)
    expect(within(outputs).getByRole('textbox', { name: 'total (decimal)' })).toHaveAccessibleDescription(outputHelp)
    expect(within(outputs).getByRole('combobox', { name: 'confirmed (boolean)' })).toHaveAccessibleDescription(
      outputHelp,
    )
  })

  it('posts a resolve as succeeded with the reason and the typed outputs', async () => {
    const fetchMock = stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: 'The order exists' } })
    await userEvent.click(within(dialog).getByLabelText('Succeeded'))
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'orderNumber (text)' }), {
      target: { value: 'PO-2026-0001' },
    })
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'total (decimal)' }), { target: { value: '1250.50' } })
    await userEvent.click(within(dialog).getByRole('combobox', { name: 'confirmed (boolean)' }))
    await userEvent.click(await screen.findByTitle('true'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(post[0]).toBe(`${instancesPath}/${instanceId}/calls/${resolvableCallId}/resolve`)
    const body = post[1]!.body as string
    expect(body).toContain('"total":1250.50')
    expect(JSON.parse(body)).toEqual({
      reason: 'The order exists',
      outcome: 'succeeded',
      output: { orderNumber: 'PO-2026-0001', total: 1250.5, confirmed: true },
    })
  })

  it('sends a failed resolve without outputs, and blocks a missing outcome or required output', async () => {
    const fetchMock = stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), {
      target: { value: 'No order was found' },
    })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))
    expect(await within(dialog).findByText('Choose an outcome.')).toBeInTheDocument()

    await userEvent.click(within(dialog).getByLabelText('Succeeded'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))
    expect(await within(dialog).findByText('Enter a value.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)

    await userEvent.click(within(dialog).getByLabelText('Failed'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(JSON.parse(post[1]!.body as string)).toEqual({ reason: 'No order was found', outcome: 'failed' })
  })

  it('shows the message of a 400 under the output field it names, and keeps the dialog open', async () => {
    stubApi({
      detail: { status: 200, body: detailWithResolvable() },
      resolve: {
        status: 400,
        body: JSON.stringify({
          title: 'The request has errors.',
          errors: { '/output/orderNumber': ['Must be text.'] },
        }),
      },
    })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: 'Try' } })
    await userEvent.click(within(dialog).getByLabelText('Succeeded'))
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'orderNumber (text)' }), { target: { value: '12' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    const field = formItem(within(dialog).getByRole('textbox', { name: 'orderNumber (text)' }))
    expect(await within(field).findByText('Must be text.')).toBeInTheDocument()
    expect(within(dialog).getByTestId('inspector-command-error')).toHaveTextContent('The request has errors.')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('shows a message on an undeclared output after the title, and keeps the dialog open', async () => {
    stubApi({
      detail: { status: 200, body: detailWithResolvable() },
      resolve: {
        status: 400,
        body: JSON.stringify({
          title: 'The request has errors.',
          errors: { '/output/unknown': ['Not declared.'] },
        }),
      },
    })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), { target: { value: 'Try' } })
    await userEvent.click(within(dialog).getByLabelText('Succeeded'))
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'orderNumber (text)' }), { target: { value: '12' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByTestId('inspector-command-error')).toHaveTextContent(
      'The request has errors. Not declared.',
    )
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('shows the Resolve button only on a call the server marks resolvable', async () => {
    stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    expect(screen.getAllByTestId(/^inspector-resolve-/).map((button) => button.getAttribute('data-testid'))).toEqual([
      `inspector-resolve-${resolvableCallId}`,
    ])
    expect(screen.getByTestId(`inspector-resolve-${resolvableCallId}`)).toHaveTextContent('Resolve')
  })

  it('shows no Resolve button when no call is resolvable', async () => {
    stubApi()

    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByTestId(/^inspector-resolve-/)).not.toBeInTheDocument()
  })
  it('offers every column of the tasks in Filter', async () => {
    stubApi()
    renderHistory()
    await screen.findByTestId('inspector-instance')
    await userEvent.click(
      within(screen.getByTestId('studio-section-inspector-tasks')).getByRole('button', { name: 'Filter' }),
    )
    await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Column' }))
    expect(optionTexts()).toEqual(['Step', 'Assignee', 'State', 'Outcome', 'Completed by', 'Started', 'Completed'])
  })

  it('keeps only the tasks that match the filter in the address', async () => {
    stubApi({
      detail: {
        status: 200,
        body: detailWithTasks({ assignee: 'clerk', state: 'completed', outcome: 'approve', completedBy: 'ann' }),
      },
    })
    renderHistory(filterSearch('tasks.', 'state', 'completed'))

    const tasks = within((await screen.findAllByTestId('studio-section-inspector-tasks')).at(-1)!)
    expect(await tasks.findByRole('row', { name: /role: clerk/ })).toBeInTheDocument()
    expect(tasks.queryByRole('row', { name: /role: manager/ })).not.toBeInTheDocument()
  })

  it('leaves the tasks and the sub-processes unchanged under a history filter that matches nothing', async () => {
    const detail = JSON.parse(detailWithRelatives()) as Record<string, unknown>
    stubApi({
      detail: {
        status: 200,
        body: JSON.stringify({ ...detail, tasks: (JSON.parse(detailText) as { tasks: unknown[] }).tasks }),
      },
    })

    renderHistory(filterSearch('history.', 'step', 'nothing'))

    await screen.findByTestId('inspector-instance')
    expect(
      within(screen.getByTestId('studio-section-inspector-history')).getByText('No history yet.'),
    ).toBeInTheDocument()
    expect(
      within(screen.getByTestId('studio-section-inspector-tasks')).getByRole('row', { name: /role: manager/ }),
    ).toBeInTheDocument()
    expect(
      within(screen.getByTestId('studio-section-inspector-sub-processes')).getByRole('row', { name: /ChildJob/ }),
    ).toBeInTheDocument()
  })

  it("filters one call's attempts under its own prefix and leaves another call's attempts shown", async () => {
    const detail = JSON.parse(detailText) as {
      calls: { id: string; attempts: Record<string, unknown>[] }[]
    }
    detail.calls[1].attempts = [{ ...detail.calls[0].attempts[0], outcome: 'succeeded', httpStatus: 200 }]
    stubApi({ detail: { status: 200, body: JSON.stringify(detail) } })

    renderHistory(filterSearch(`attempts.${detail.calls[0].id}.`, 'outcome', 'succeeded'))

    await screen.findByTestId('inspector-instance')
    const first = within(screen.getByTestId(`inspector-call-${detail.calls[0].id}`))
    const second = within(screen.getByTestId(`inspector-call-${detail.calls[1].id}`))
    expect(first.queryByRole('row', { name: /retryable/ })).not.toBeInTheDocument()
    expect(second.getByRole('row', { name: /succeeded/ })).toBeInTheDocument()
  })

  it('filters a date column by the local day of its time', async () => {
    const createdAt = '2026-10-12T01:00:00Z'
    stubApi({ detail: { status: 200, body: detailWithTasks({ assignee: 'clerk', createdAt }) } })

    renderHistory(filterSearch('tasks.', 'createdAt', dayjs(createdAt).format('YYYY-MM-DD')))

    const tasks = within(await screen.findByTestId('studio-section-inspector-tasks'))
    expect(await tasks.findByRole('row', { name: /role: clerk/ })).toBeInTheDocument()
    expect(tasks.queryByRole('row', { name: /role: manager/ })).not.toBeInTheDocument()
  })

  it('shows the input as a table of names and values, with a hidden value as Hidden', async () => {
    stubApi({ detail: { status: 200, body: detailWithData({ input: startedWith }) } })

    renderPage('?tab=data')

    const data = await screen.findByTestId('inspector-data')
    expect(screen.getByRole('tab', { name: 'Data' })).toHaveAttribute('aria-selected', 'true')
    const input = within(data).getByTestId('inspector-input')
    expect(within(input).getByRole('cell', { name: 'Laptops' })).toBeInTheDocument()
    const budget = within(input).getByRole('cell', { name: 'budget' }).closest('tr')!
    expect(within(budget).getByText('Hidden')).toBeInTheDocument()
    expect(within(data).queryByText('It started with no input.')).not.toBeInTheDocument()
  })

  it('says the instance has not ended yet in Output while it waits or runs', async () => {
    stubApi({ detail: { status: 200, body: detailWithData({ input: startedWith }) } })

    renderPage('?tab=data')

    const data = await screen.findByTestId('inspector-data')
    expect(within(data).getByText('Output')).toBeInTheDocument()
    expect(within(data).getByText('The instance has not ended yet.')).toBeInTheDocument()
  })

  it('says it started with no input when the instance has none, or its process has a subject', async () => {
    stubApi({ detail: { status: 200, body: detailWithData({ input: [] }) } })
    const { unmount } = renderPage('?tab=data')
    expect(await screen.findByText('It started with no input.')).toBeInTheDocument()
    unmount()

    stubApi()
    renderPage('?tab=data')
    expect(await screen.findByText('It started with no input.')).toBeInTheDocument()
  })

  it('shows the end step and the values a completed instance ended with', async () => {
    stubApi({
      detail: {
        status: 200,
        body: detailWithData({
          state: 'completed',
          step: 'done',
          endedAt: '2026-10-10T02:06:00Z',
          input: [],
          result: { step: 'done', values: [{ name: 'status', value: 'approved', hidden: false }] },
        }),
      },
    })

    renderPage('?tab=data')

    const data = await screen.findByTestId('inspector-data')
    expect(within(data).getByText('Ended at done.')).toBeInTheDocument()
    expect(
      within(within(data).getByTestId('inspector-output')).getByRole('cell', { name: 'approved' }),
    ).toBeInTheDocument()
    expect(within(data).queryByText('The instance has not ended yet.')).not.toBeInTheDocument()
  })

  it('reads Not set in Output for a completed instance whose values were not kept, and for a failed or cancelled one', async () => {
    for (const patch of [
      { state: 'completed', step: 'done', result: { step: 'done', values: null } },
      { state: 'failed', result: null },
      { state: 'cancelled', result: null },
    ]) {
      stubApi({ detail: { status: 200, body: detailWithData({ input: startedWith, ...patch }) } })
      const { unmount } = renderPage('?tab=data')

      const data = await screen.findByTestId('inspector-data')
      const output = within(data).getByText('Output').closest('div')!
      expect(within(output).getByText('Not set')).toBeInTheDocument()
      expect(within(data).queryByText('The instance has not ended yet.')).not.toBeInTheDocument()
      unmount()
    }
  })
})
