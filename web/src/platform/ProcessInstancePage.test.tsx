import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { chooseOption, emptyDescription, formItem, optionTexts } from '../test/antd'
import { ShellAsideHost } from '../test/shellAsideHost'
import { dialog } from '../test/tableFilter'
import { changeTint } from './changeTint'
import { designTokensFor } from './designTokens'
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
  "timerDueAt": null,
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
      "assigneeId": null,
      "state": "open",
      "outcome": null,
      "completedBy": null,
      "completedById": null,
      "createdAt": "2026-10-10T02:05:00.123456Z",
      "dueAt": "2026-10-11T02:05:00.123456Z",
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

/** The instance waiting for the event `OrderShipped` at its only wait, whose event has the label key `eventLabelKey`, or none. */
function detailWaitingForEvent(eventLabelKey: string | null) {
  const detail = JSON.parse(detailText) as Record<string, unknown>
  return JSON.stringify({
    ...detail,
    state: 'waiting',
    step: 'waitShipped',
    tasks: [],
    calls: [],
    graph: {
      start: 'waitShipped',
      steps: [
        {
          name: 'waitShipped',
          type: 'waitEvent',
          labelKey: null,
          event: 'OrderShipped',
          eventLabelKey,
          transitions: [{ next: 'done', on: null, branch: null, labelKey: null, after: null }],
        },
        { name: 'done', type: 'end', labelKey: null, event: null, eventLabelKey: null, transitions: [] },
      ],
    },
    path: [
      {
        step: 'waitShipped',
        startedAt: '2026-10-10T02:05:00.123456Z',
        finishedAt: null,
        decision: null,
        output: null,
        error: null,
        problem: null,
        traceId: null,
      },
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
          <ShellAsideHost>
            <ProcessInstancePage
              sitePath="e2e"
              pages={['Inspector', 'Children']}
              widget={inspectorWidget}
              instanceId={instanceId}
              title="Approvals"
              locale="en"
              fallbackLocale="en"
            />
          </ShellAsideHost>
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

/** The page opened on its History tab, with `search` joined to the address's own, such as a table's filter. */
function renderHistory(search = '') {
  return renderPage(`?tab=history${search === '' ? '' : `&${search.slice(1)}`}`)
}

/**
 * The detail text with four runs, newest first: `registerPurchase` that is open, `managerApproval` run again on the 12th,
 * `registerPurchase` that failed after 3 seconds, and the first `managerApproval`.
 */
function detailRuns() {
  const detail = JSON.parse(detailText) as { path: Record<string, unknown>[] }
  const [first] = detail.path
  detail.path.push(
    {
      ...first,
      step: 'registerPurchase',
      startedAt: '2026-10-10T04:00:00.000000Z',
      finishedAt: '2026-10-10T04:00:03.000000Z',
      traceId: null,
      error: 'boom',
    },
    {
      ...first,
      step: 'managerApproval',
      startedAt: '2026-10-12T01:00:00.000000Z',
      finishedAt: '2026-10-12T01:00:00.100000Z',
      traceId: null,
    },
  )
  return JSON.stringify(detail)
}

/** The address part that keeps `filter`, a node of the record API's filter, under the History table's prefix. */
function historyFilter(...nodes: Record<string, unknown>[]) {
  return `?history.filter=${encodeURIComponent(JSON.stringify({ all: nodes }))}`
}

/** Opens the rows of the run of `step` numbered `number`, from the fold on its History row. */
async function openRun(step: string, number = 1) {
  const link = await screen.findByTestId(`inspector-history-step-${step}-${number}`)
  await userEvent.click(within(link.closest('tr')!).getByRole('button', { name: 'Show the rows in it' }))
}

/** The runs on the History table's page, as `step-number`, in the order they show. */
function shownRuns() {
  return screen
    .queryAllByTestId(/^inspector-history-step-/)
    .map((link) => link.getAttribute('data-testid')!.replace('inspector-history-step-', ''))
}

describe('ProcessInstancePage', () => {
  it('shows no record fact, the parent in Started by, and the sub-process of a run by its process title', async () => {
    stubApi({ detail: { status: 200, body: detailWithRelatives() } })

    renderHistory()

    const facts = await screen.findByTestId('studio-section-inspector-facts')
    expect(within(facts).queryByText('Record')).not.toBeInTheDocument()
    const parentLink = within(facts).getByTestId('inspector-parent-link')
    expect(parentLink).toHaveTextContent('Purchase request approval')
    expect(parentLink).toHaveAttribute('href', `/e2e/inspector/${parentId}`)
    expect(screen.queryByTestId(`inspector-subprocess-${childId}`)).not.toBeInTheDocument()
    await openRun('wait')
    const childLink = await screen.findByTestId(`inspector-subprocess-${childId}`)
    expect(childLink).toHaveAttribute('href', `/e2e/children/${childId}`)
    expect(childLink).toHaveTextContent('ChildJob')
    const childRow = childLink.closest('tr')!
    expect(within(childRow).getByText('Started ChildJob.')).toBeInTheDocument()
    expect(within(childRow).getByText('Running').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
  })

  it('shows no parent and no sub-process link for an instance without relatives', async () => {
    stubApi()

    renderHistory()

    await openRun('registerPurchase')
    expect(screen.queryByTestId('inspector-parent-link')).not.toBeInTheDocument()
    expect(screen.queryByTestId(/^inspector-subprocess-/)).not.toBeInTheDocument()
  })

  it('shows the open task in the rows of its run, with Waits for its assignee and the state Open', async () => {
    stubApi()

    renderHistory()

    await screen.findByTestId('inspector-instance')
    const runRow = screen.getByTestId('inspector-history-step-managerApproval-1').closest('tr')!
    expect(screen.getAllByRole('row', { name: /Waits for manager\./ })).toEqual([runRow])
    await openRun('managerApproval')
    const [, taskRow] = screen.getAllByRole('row', { name: /Waits for manager\./ })
    expect(within(taskRow).getByText('Task')).toBeInTheDocument()
    expect(within(taskRow).getByText('Open').closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
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
    expect(screen.getByTestId('inspector-history')).toBeInTheDocument()

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
    expect(screen.getByTestId('inspector-history')).toBeInTheDocument()
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

    fireEvent.click(within(approval).getByRole('button', { name: 'Manager approval' }))

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

  it('shows the em dash in the Duration cell of a task that has not ended', async () => {
    stubApi()

    renderHistory()

    await openRun('managerApproval')
    const [, taskRow] = screen.getAllByRole('row', { name: /Waits for manager/ })
    const headers = within(taskRow.closest('table')!).getAllByRole('columnheader')
    const duration = headers.findIndex((header) => header.textContent?.startsWith('Duration'))
    expect(within(taskRow).getAllByRole('cell')[duration].textContent).toBe('—')
  })

  it('opens a call to its attempts, each with its answer and the tone of its outcome', async () => {
    stubApi()

    renderHistory()

    await openRun('registerPurchase')
    expect(screen.queryByText('No history yet.')).not.toBeInTheDocument()
    expect(screen.queryByRole('row', { name: /Attempt 1/ })).not.toBeInTheDocument()
    const callRow = screen.getByRole('row', { name: /createOrder/ })
    await userEvent.click(within(callRow).getByRole('button', { name: 'Show the rows in it' }))
    const attemptRow = screen.getByRole('row', { name: /Attempt 1/ })
    expect(within(attemptRow).getByText('Answered HTTP 503.')).toBeInTheDocument()
    expect(within(attemptRow).getByText('Retryable').closest('[data-tone]')).toHaveAttribute('data-tone', 'muted')
  })

  it('words a failed run from its problem code, with the seconds of a timeout, and never shows a stored error message', async () => {
    const detail = JSON.parse(detailText) as {
      path: Record<string, unknown>[]
      calls: { attempts: Record<string, unknown>[] }[]
    }
    detail.path.push({
      ...detail.path[0],
      step: 'managerApproval',
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

    expect(await screen.findByText('Stopped. No answer came within 30 sec.')).toBeInTheDocument()
    expect(screen.queryByText(/did not finish/)).not.toBeInTheDocument()
    expect(screen.getByText('Stopped with an error.')).toBeInTheDocument()
    expect(screen.queryByText(/InvalidOperationException/)).not.toBeInTheDocument()
    await openRun('registerPurchase')
    const callRow = screen.getByRole('row', { name: /createOrder/ })
    await userEvent.click(within(callRow).getByRole('button', { name: 'Show the rows in it' }))
    const attempt = screen.getByRole('row', { name: /Attempt 1/ })
    expect(within(attempt).getByText('The outside system answered with a server error.')).toBeInTheDocument()
    expect(within(attempt).queryByText(/answered with the status 503/)).not.toBeInTheDocument()
  })

  it('shows no stored error message for a code this build does not know', async () => {
    const detail = JSON.parse(detailText) as { path: Record<string, unknown>[] }
    detail.path[0].error = 'A fixed text.'
    detail.path[0].problem = { code: 'somethingNew', values: {} }
    stubApi({ detail: { status: 200, body: JSON.stringify(detail) } })

    renderHistory()

    expect(await screen.findByText('Stopped with an error.')).toBeInTheDocument()
    expect(screen.queryByText('A fixed text.')).not.toBeInTheDocument()
  })

  it('reads No answer came. for an attempt with no HTTP status and no problem', async () => {
    const detail = JSON.parse(detailText) as { calls: { attempts: { httpStatus: number | null }[] }[] }
    detail.calls[0].attempts[0].httpStatus = null
    stubApi({ detail: { status: 200, body: JSON.stringify(detail) } })

    renderHistory()

    await openRun('registerPurchase')
    const callRow = screen.getByRole('row', { name: /createOrder/ })
    await userEvent.click(within(callRow).getByRole('button', { name: 'Show the rows in it' }))
    expect(within(screen.getByRole('row', { name: /Attempt 1/ })).getByText('No answer came.')).toBeInTheDocument()
  })

  it("names a call by its operation and its connector, and shows none of its input, which is the panel's", async () => {
    stubApi()

    renderHistory()

    await openRun('registerPurchase')
    const callRow = screen.getByText('createOrder').closest('tr')!
    expect(within(callRow).getByText('createOrder (PurchasingSystem) is in progress.')).toBeInTheDocument()
    expect(screen.queryByText('Input: Hidden')).not.toBeInTheDocument()
    expect(screen.queryByText('Laptops')).not.toBeInTheDocument()
  })

  it('shows the operation of a call as plain text, and its state as a tag in words with the tone of its meaning', async () => {
    stubApi()

    renderHistory()

    await openRun('registerPurchase')
    const hidden = within(screen.getByText('createOrder').closest('tr')!)
    expect(hidden.getByText('createOrder').closest('.ant-tag')).toBeNull()
    expect(hidden.getByText('Pending').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
    const visible = within(screen.getByText('reference').closest('tr')!)
    expect(visible.getByText('reference').closest('.ant-tag')).toBeNull()
    expect(visible.getByText('Succeeded').closest('[data-tone]')).toHaveAttribute('data-tone', 'success')
    expect(visible.getByText('reference (PaymentReferences) succeeded.')).toBeInTheDocument()
  })

  it('names the tone of each call state, and shows a state it does not know as sent', async () => {
    const detail = JSON.parse(detailText) as { calls: { state: string }[] }
    detail.calls[0].state = 'unknown'
    detail.calls[1].state = 'archived'
    stubApi({ detail: { status: 200, body: JSON.stringify(detail) } })

    renderHistory()

    await openRun('registerPurchase')
    const first = within(screen.getByText('createOrder').closest('tr')!)
    expect(first.getByText('Outcome unknown').closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
    expect(first.getByText('createOrder (PurchasingSystem) has an unknown outcome.')).toBeInTheDocument()
    const second = within(screen.getByText('reference').closest('tr')!)
    expect(second.getByText('archived').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
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

  it('names a waited event on the diagram by its label from the release texts', async () => {
    stubApi({
      detail: { status: 200, body: detailWaitingForEvent('events.orderShipped') },
      releaseTexts: {
        status: 200,
        body: JSON.stringify({ locale: 'en', texts: { 'events.orderShipped': 'Order shipped' } }),
      },
    })

    renderPage()

    expect(await screen.findByText('Order shipped')).toBeInTheDocument()
    expect(screen.queryByText('OrderShipped')).not.toBeInTheDocument()
  })

  it('names a waited event by its label in History and in Waits for', async () => {
    stubApi({
      detail: { status: 200, body: detailWaitingForEvent('events.orderShipped') },
      releaseTexts: {
        status: 200,
        body: JSON.stringify({ locale: 'en', texts: { 'events.orderShipped': 'Order shipped' } }),
      },
    })

    renderPage('?tab=history&step=waitShipped')

    expect(await screen.findByRole('row', { name: /Waits for Order shipped\./ })).toBeInTheDocument()
    expect((await stepPanel()).getByText('Order shipped')).toBeVisible()
  })

  it('names a waited event by its name in Waits for when its event has no label', async () => {
    stubApi({ detail: { status: 200, body: detailWaitingForEvent(null) } })

    renderPage('?step=waitShipped')

    expect((await stepPanel()).getByText('OrderShipped')).toBeVisible()
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

  it('shows History as one table of runs, newest first, with the commands on the heading', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['cancel']) } })

    renderHistory()

    await screen.findByTestId('inspector-instance')
    expect(within(screen.getByTestId('page-actions')).getByTestId('inspector-command-cancel')).toBeInTheDocument()
    const table = within(screen.getByTestId('inspector-history'))
    expect(table.getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      'Step',
      'What happened',
      'State',
      'Started',
      'Duration',
    ])
    expect(shownRuns()).toEqual(['registerPurchase-1', 'managerApproval-1'])
    for (const title of ['Tasks', 'Calls', 'Sub-processes']) {
      expect(screen.queryByRole('region', { name: title })).not.toBeInTheDocument()
    }
  })

  it('reads each run as a sentence: its last call, its task, and the step it went on to', async () => {
    stubApi()

    renderHistory()

    const open = (await screen.findByTestId('inspector-history-step-registerPurchase-1')).closest('tr')!
    expect(within(open).getByText('Calls a connector')).toBeInTheDocument()
    expect(within(open).getByText('reference (PaymentReferences) succeeded.')).toBeInTheDocument()
    const first = screen.getByTestId('inspector-history-step-managerApproval-1').closest('tr')!
    expect(within(first).getByText('Waits for manager.')).toBeInTheDocument()
  })

  it('reads a run whose connector call succeeded on its third attempt, and opens the attempts and the lookup', async () => {
    const [retryable, succeeded] = connectorCall().attempts
    const attempts = [
      retryable,
      { ...retryable, number: 2, startedAt: '2026-10-10T04:00:05.000000Z', finishedAt: '2026-10-10T04:00:06.000000Z' },
      { ...succeeded, number: 3, startedAt: '2026-10-10T04:00:10.000000Z' },
      { ...succeeded, kind: 'lookup', number: 1, outcome: 'found', startedAt: '2026-10-10T04:00:12.000000Z' },
    ]
    const call = connectorCall({ connector: 'ERP', operation: 'createPurchaseOrder', attempts })
    stubApi({ detail: { status: 200, body: detailForRun({ calls: [call] }) } })

    renderHistory()

    const run = (await screen.findByTestId('inspector-history-step-registerPurchase-1')).closest('tr')!
    expect(within(run).getByText('createPurchaseOrder (ERP) succeeded on attempt 3.')).toBeVisible()
    await openRun('registerPurchase')
    const callRow = screen.getByText('createPurchaseOrder').closest('tr')!
    expect(within(callRow).getByText('createPurchaseOrder (ERP) succeeded on attempt 3.')).toBeVisible()
    await userEvent.click(within(callRow).getByRole('button', { name: 'Show the rows in it' }))
    const names = screen.getAllByRole('row').flatMap((row) => /(Attempt|Lookup) \d/.exec(row.textContent!)?.[0] ?? [])
    expect(names).toEqual(['Lookup 1', 'Attempt 3', 'Attempt 2', 'Attempt 1'])
    expect(within(screen.getByRole('row', { name: /Lookup 1/ })).getByText('Found')).toBeVisible()
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

  it('puts the focus on the empty reason and sends nothing', async () => {
    const fetchMock = stubApi({ detail: { status: 200, body: detailWith(['cancel']) } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument()
    expect(within(dialog).getByRole('textbox', { name: 'Reason' })).toHaveFocus()
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

    await openRun('registerPurchase')
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

    await openRun('registerPurchase')
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

    await openRun('registerPurchase')
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

  it('puts the focus on the empty reason of a resolve and sends nothing', async () => {
    const fetchMock = stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderHistory()

    await openRun('registerPurchase')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument()
    expect(within(dialog).getByRole('textbox', { name: 'Reason' })).toHaveFocus()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)
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

    await openRun('registerPurchase')
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

    await openRun('registerPurchase')
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

  it('shows the Resolve button on the row of a call the server marks resolvable, and on no other row', async () => {
    stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderHistory()

    await openRun('registerPurchase')
    expect(screen.getAllByTestId(/^inspector-resolve-/).map((button) => button.getAttribute('data-testid'))).toEqual([
      `inspector-resolve-${resolvableCallId}`,
    ])
    const button = screen.getByTestId(`inspector-resolve-${resolvableCallId}`)
    expect(button).toHaveTextContent('Resolve')
    expect(button.closest('tr')).toBe(screen.getByText('createOrder').closest('tr'))
    expect(within(screen.getByText('reference').closest('tr')!).queryByRole('button', { name: 'Resolve' })).toBeNull()
    expect(
      within(screen.getByTestId('inspector-history-step-registerPurchase-1').closest('tr')!).queryByRole('button', {
        name: 'Resolve',
      }),
    ).toBeNull()
  })

  it('shows no Resolve button when no call is resolvable', async () => {
    stubApi()

    renderHistory()

    await openRun('registerPurchase')
    expect(screen.queryByTestId(/^inspector-resolve-/)).not.toBeInTheDocument()
  })

  it('offers Step, State, Started and Duration in Filter', async () => {
    stubApi()
    renderHistory()
    await screen.findByTestId('inspector-instance')

    await userEvent.click(within(screen.getByTestId('inspector-history')).getByRole('button', { name: 'Filter' }))
    await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Column' }))

    expect(optionTexts()).toEqual(['Step', 'State', 'Started', 'Duration'])
  })

  it('keeps only the runs in the State that the filter in the address names', async () => {
    stubApi({ detail: { status: 200, body: detailRuns() } })

    renderHistory(historyFilter({ field: 'state', op: 'eq', value: 'failed' }))

    await screen.findByTestId('inspector-history-step-registerPurchase-1')
    expect(shownRuns()).toEqual(['registerPurchase-1'])
  })

  it('filters Started by the local day of the time of a run', async () => {
    const day = dayjs('2026-10-12T01:00:00Z').format('YYYY-MM-DD')
    stubApi({ detail: { status: 200, body: detailRuns() } })

    renderHistory(historyFilter({ field: 'startedAt', op: 'eq', value: day }))

    await screen.findByTestId('inspector-history-step-managerApproval-2')
    expect(shownRuns()).toEqual(['managerApproval-2'])
  })

  it('filters Duration in seconds', async () => {
    stubApi({ detail: { status: 200, body: detailRuns() } })

    renderHistory(historyFilter({ field: 'duration', op: 'gte', value: 3 }))

    await screen.findByTestId('inspector-history-step-registerPurchase-1')
    expect(shownRuns()).toEqual(['registerPurchase-1'])
  })

  it('shows the empty text under a filter that matches no run', async () => {
    stubApi({ detail: { status: 200, body: detailWithRelatives() } })

    renderHistory(historyFilter({ field: 'step', op: 'eq', value: 'nothing' }))

    expect(await screen.findByText('No history yet.')).toBeInTheDocument()
    expect(shownRuns()).toEqual([])
  })

  it('finds a run by its own words and by the words of a row inside it, and keeps the search in the address', async () => {
    stubApi()
    renderHistory()
    await screen.findByTestId('inspector-history-step-registerPurchase-1')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'createOrder{Enter}')

    await waitFor(() => expect(shownRuns()).toEqual(['registerPurchase-1']))
    expect(screen.getByTestId('address')).toHaveTextContent('history.search=createOrder')
  })

  it('sorts the runs by State, Failed then Waiting then Done, and keeps the sort as history.sort', async () => {
    stubApi({ detail: { status: 200, body: detailRuns() } })
    renderHistory()
    await screen.findByTestId('inspector-history-step-registerPurchase-1')
    expect(shownRuns()).toEqual(['registerPurchase-2', 'managerApproval-2', 'registerPurchase-1', 'managerApproval-1'])

    await userEvent.click(screen.getByRole('columnheader', { name: 'State' }))

    await waitFor(() => expect(screen.getByTestId('address')).toHaveTextContent('history.sort=state'))
    expect(shownRuns()).toEqual(['registerPurchase-1', 'registerPurchase-2', 'managerApproval-2', 'managerApproval-1'])
    const states = screen
      .getAllByTestId(/^inspector-history-step-/)
      .map((link) => within(link.closest('tr')!).getAllByText(/^(Failed|Waiting for an event|Done)$/)[0].textContent)
    expect(states).toEqual(['Failed', 'Waiting for an event', 'Done', 'Done'])
  })

  it('sorts the runs by Started from the address, newest first when it is descending', async () => {
    stubApi({ detail: { status: 200, body: detailRuns() } })

    renderPage('?tab=history&history.sort=-startedAt')

    await screen.findByTestId('inspector-history-step-registerPurchase-1')
    expect(shownRuns()).toEqual(['managerApproval-2', 'registerPurchase-1', 'managerApproval-1', 'registerPurchase-2'])
  })

  it('opens a run in the side panel from its step, keeping step and run in the address next to the tab', async () => {
    stubApi({ detail: { status: 200, body: detailRuns() } })
    renderHistory()

    await userEvent.click(await screen.findByTestId('inspector-history-step-managerApproval-1'))

    const panel = await stepPanel()
    expect(panel.getByRole('heading', { name: 'Manager approval' })).toBeVisible()
    expect(screen.getByTestId('address')).toHaveTextContent('tab=history')
    expect(screen.getByTestId('address')).toHaveTextContent('step=managerApproval&run=1')
  })

  it('reads the instance again from Refresh on the timeline', async () => {
    const fetchMock = stubApi()
    renderHistory()
    await screen.findByTestId('inspector-history-step-registerPurchase-1')
    const reads = () => fetchMock.mock.calls.filter(([url]) => url === `${instancesPath}/${instanceId}`).length
    const before = reads()

    await userEvent.click(within(screen.getByTestId('inspector-history')).getByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(reads()).toBe(before + 1))
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

/** The panel of a step's run, once the page has opened it. */
const connectorCallId = '0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b9e'

/** A connector call of `registerPurchase` that sent a request, was retried once and then succeeded. */
function connectorCall(patch: Record<string, unknown> = {}) {
  return {
    id: connectorCallId,
    kind: 'connector',
    step: 'registerPurchase',
    connector: 'PurchasingSystem',
    operation: 'createOrder',
    state: 'succeeded',
    attempts: [
      {
        kind: 'attempt',
        number: 1,
        startedAt: '2026-10-10T04:00:01.000000Z',
        finishedAt: '2026-10-10T04:00:03.000000Z',
        outcome: 'retryable',
        httpStatus: 503,
        error: null,
        problem: null,
        traceId: '0af7651916cd43dd8448eb211c80319c',
        response: { status: 503, headers: {}, body: null, truncated: false },
      },
      {
        kind: 'attempt',
        number: 2,
        startedAt: '2026-10-10T04:00:10.000000Z',
        finishedAt: '2026-10-10T04:00:11.500000Z',
        outcome: 'succeeded',
        httpStatus: 201,
        error: null,
        problem: null,
        traceId: '1bf7651916cd43dd8448eb211c80319d',
        response: {
          status: 201,
          headers: { 'content-type': 'application/json' },
          body: '{"orderId":"PO-77"}',
          truncated: false,
        },
      },
    ],
    input: null,
    inputHidden: false,
    request: {
      method: 'POST',
      url: 'https://erp.example/orders',
      query: { dryRun: 'false' },
      headers: { 'x-source': 'axis' },
      body: '{"title":"Laptops","lines":2}',
      truncated: false,
    },
    returned: null,
    resolvable: false,
    ...patch,
  }
}

/**
 * The detail text with `calls` in place of its own, and with a path entry for `registerPurchase` that made `made` where
 * given. `registerPurchase` is the instance's current step.
 */
function detailForRun({ calls, made }: { calls?: unknown[]; made?: Record<string, unknown> }) {
  const detail = JSON.parse(detailText) as { calls: unknown[]; path: Record<string, unknown>[] }
  if (calls) {
    detail.calls = calls
  }
  if (made) {
    detail.path.push({
      step: 'registerPurchase',
      startedAt: '2026-10-10T04:00:00.000000Z',
      finishedAt: '2026-10-10T04:00:03.000000Z',
      decision: null,
      output: null,
      error: null,
      problem: null,
      traceId: null,
      read: null,
      made: { changes: null, event: null, notified: null, created: null, ...made },
    })
  }
  return JSON.stringify(detail)
}

/** The run of `detailForRun` at `registerPurchase`, which raised the event `OrderPlaced` with the label key `eventLabelKey`, or none. */
function detailRaisingEvent(eventLabelKey: string | null) {
  const detail = JSON.parse(detailForRun({ calls: [], made: { event: { name: 'OrderPlaced', fields: [] } } })) as {
    graph: { steps: Record<string, unknown>[] }
  }
  const step = detail.graph.steps.find((candidate) => candidate.name === 'registerPurchase')!
  Object.assign(step, { event: 'OrderPlaced', eventLabelKey })
  return JSON.stringify(detail)
}

async function stepPanel() {
  return within(await screen.findByTestId('inspector-step-run'))
}

describe('ProcessInstancePage step run panel', () => {
  it("copies the whole trace ID of the run's last entry from the copy button on its Overview", async () => {
    stubApi()
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true })
    renderPage('?step=managerApproval')

    const panel = await stepPanel()
    expect(panel.getByText('4bf92f35')).toBeVisible()
    await userEvent.click(panel.getByRole('button', { name: 'Copy' }))

    expect(writeText).toHaveBeenCalledExactlyOnceWith('4bf92f3577b34da6a3ce929d0e0e4736')
  })

  it("opens a step's latest run in the side panel when its card is clicked, with the state as a tag and step in the address", async () => {
    stubApi({ detail: { status: 200, body: detailLooping() } })
    renderPage()
    const again = await screen.findByTestId('rf__node-again', undefined, { timeout: 5000 })
    expect(screen.queryByTestId('inspector-step-run')).not.toBeInTheDocument()

    fireEvent.click(within(again).getByRole('button', { name: 'again' }))

    const panel = await stepPanel()
    expect(panel.getByRole('heading', { name: 'again' })).toBeVisible()
    expect(panel.getByText('Done').closest('[data-tone]')).toHaveAttribute('data-tone', 'success')
    expect(panel.getByText('Operation')).toBeVisible()
    expect(screen.getByTestId('address')).toHaveTextContent(/^\?step=again$/)
  })

  it('keeps run in the address for an earlier run that the run chooser picks', async () => {
    stubApi({ detail: { status: 200, body: detailLooping() } })
    renderPage()
    const again = await screen.findByTestId('rf__node-again', undefined, { timeout: 5000 })
    fireEvent.click(within(again).getByRole('button', { name: 'again' }))
    const panel = await stepPanel()

    await chooseOption(
      'Run 1',
      panel.getByRole('combobox', { name: 'Choose a run' }).closest('.ant-select') as HTMLElement,
    )

    expect(screen.getByTestId('address')).toHaveTextContent(/^\?step=again&run=1$/)
    expect(panel.getByRole('combobox', { name: 'Choose a run' }).closest('.ant-select')).toHaveTextContent('Run 1')
  })

  it('shows no run chooser for a step that ran once', async () => {
    stubApi({ detail: { status: 200, body: detailLooping() } })
    renderPage('?step=done')

    const panel = await stepPanel()

    expect(panel.queryByRole('combobox')).not.toBeInTheDocument()
  })

  it('shuts the panel on Close, takes step out of the address and keeps the step chosen', async () => {
    stubApi()
    renderPage('?step=managerApproval')
    await stepPanel()

    await userEvent.click(screen.getByTestId('inspector-step-run-close'))

    expect(screen.queryByTestId('inspector-step-run')).not.toBeInTheDocument()
    expect(screen.getByTestId('address')).toHaveTextContent(/^$/)
    const approval = await screen.findByTestId('rf__node-managerApproval', undefined, { timeout: 5000 })
    expect(within(approval).getByRole('button', { name: 'Manager approval' })).toHaveAttribute('aria-current', 'true')
  })

  it('opens the panel on the step that the address names', async () => {
    stubApi()

    renderPage('?step=managerApproval')

    const panel = await stepPanel()
    expect(panel.getByRole('heading', { name: 'Manager approval' })).toBeVisible()
    expect(panel.getByText('Task')).toBeVisible()
    const approval = await screen.findByTestId('rf__node-managerApproval', undefined, { timeout: 5000 })
    expect(within(approval).getByRole('button', { name: 'Manager approval' })).toHaveAttribute('aria-current', 'true')
  })

  it('opens no panel for a step that the graph does not have', async () => {
    stubApi()

    renderPage('?step=nowhere')

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByTestId('inspector-step-run')).not.toBeInTheDocument()
  })

  it("shows the run's times, task and call facts, and calls an operation by what it does", async () => {
    stubApi()

    renderPage('?step=registerPurchase')

    const panel = await stepPanel()
    expect(panel.getByText('Calls a connector')).toBeVisible()
    expect(panel.getByText('Connector')).toBeVisible()
    expect(panel.getByText('PurchasingSystem')).toBeVisible()
    expect(panel.getByText('createOrder')).toBeVisible()
    expect(panel.getByText('Came from')).toBeVisible()
    expect(panel.getByRole('link', { name: 'Manager approval' })).toHaveAttribute(
      'href',
      '/e2e/inspector/' + instanceId + '?step=managerApproval',
    )
    expect(panel.getAllByRole('tab').map((tab) => tab.textContent)).toEqual(['Overview', 'Input', 'Output', 'Attempts'])
  })

  it("shows a task's assignee and its due time with Overdue while it is open past due", async () => {
    stubApi()

    renderPage('?step=managerApproval')

    const panel = await stepPanel()
    expect(panel.getByText('Assigned to')).toBeVisible()
    expect(panel.getByText('manager')).toBeVisible()
    expect(panel.getByText('Due')).toBeVisible()
    expect(panel.getByText('Overdue').closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
    expect(panel.getByText('Completed by')).toBeVisible()
  })

  it('has an Error tab for a failed run that words its problem', async () => {
    stubApi({ detail: { status: 200, body: detailFailedAt({ code: 'refused', values: {} }) } })

    renderPage('?step=registerPurchase')

    const panel = await stepPanel()
    expect(panel.getByRole('tab', { name: 'Overview' })).toHaveAttribute('aria-selected', 'true')
    await userEvent.click(panel.getByRole('tab', { name: 'Error' }))
    expect(panel.getByText('The outside system refused the call.')).toBeVisible()
    expect(screen.getByTestId('address')).toHaveTextContent('stepTab=error')
  })

  it('opens the Error tab from the address, and shows the stored error when the run has no problem code', async () => {
    stubApi({ detail: { status: 200, body: detailFailedAt(null, 'Connection reset') } })

    renderPage('?step=registerPurchase&stepTab=error')

    const panel = await stepPanel()
    expect(panel.getByRole('tab', { name: 'Error' })).toHaveAttribute('aria-selected', 'true')
    expect(panel.getByText('Connection reset')).toBeVisible()
  })

  it('has no Error tab and no tab bar for a run that did not fail', async () => {
    stubApi()

    renderPage('?step=managerApproval&stepTab=error')

    const panel = await stepPanel()
    expect(panel.queryByRole('tab', { name: 'Error' })).not.toBeInTheDocument()
    expect(panel.queryByRole('tablist')).not.toBeInTheDocument()
  })

  it('names the child of a sub-process step by its process title, as a link to its inspector page', async () => {
    stubApi({ detail: { status: 200, body: detailWithRelatives() } })

    renderPage('?step=wait')

    const link = await screen.findByTestId(`inspector-run-subprocess-${childId}`)
    expect(link).toHaveTextContent('ChildJob')
    expect(link).toHaveAttribute('href', `/e2e/children/${childId}`)
    expect(within(link.closest('dd')!).getByText('Running')).toBeVisible()
  })

  it('says that a step the instance has not reached has not run', async () => {
    stubApi()

    renderPage('?step=done')

    const panel = await stepPanel()
    expect(panel.getByRole('heading', { name: 'done' })).toBeVisible()
    expect(panel.getByText('This step has not run.')).toBeVisible()
    expect(panel.queryByText('Started')).not.toBeInTheDocument()
  })

  it('opens a step through Find a step and Go to the current step', async () => {
    stubApi()
    renderPage()
    const goTo = await screen.findByRole('button', { name: 'Go to the current step' }, { timeout: 5000 })

    await userEvent.click(goTo)

    expect(await stepPanel()).toBeDefined()
    expect(screen.getByTestId('address')).toHaveTextContent(/^\?step=registerPurchase$/)
  })

  it("shows a connector call's request as its method and address on one line, with its body as a table or JSON", async () => {
    stubApi({ detail: { status: 200, body: detailForRun({ calls: [connectorCall()] }) } })

    renderPage('?step=registerPurchase&stepTab=input')

    const panel = await stepPanel()
    const line = panel.getByTestId('inspector-run-request-line')
    expect(line).toHaveTextContent('POST')
    expect(line).toHaveTextContent('https://erp.example/orders')
    expect(panel.getByText('Query parameters')).toBeVisible()
    expect(panel.getByText('dryRun')).toBeVisible()
    expect(panel.getByText('x-source')).toBeVisible()
    expect(panel.getByRole('tab', { name: 'Table' })).toHaveAttribute('aria-selected', 'true')
    expect(panel.getByText('title')).toBeVisible()
    expect(panel.getByText('Laptops')).toBeVisible()
    await userEvent.click(panel.getByRole('tab', { name: 'JSON' }))
    expect(panel.getByText(/"title": "Laptops"/)).toBeVisible()
  })

  it('reads Hidden for a request that the reader may not see, and Not set for one the call kept none of', async () => {
    stubApi({
      detail: { status: 200, body: detailForRun({ calls: [connectorCall({ request: null, inputHidden: true })] }) },
    })
    const { unmount } = renderPage('?step=registerPurchase&stepTab=input')
    const hidden = await stepPanel()
    expect(hidden.getByText('Request')).toBeVisible()
    expect(hidden.getByText('Hidden')).toBeVisible()
    unmount()

    stubApi({ detail: { status: 200, body: detailForRun({ calls: [connectorCall({ request: null })] }) } })
    renderPage('?step=registerPurchase&stepTab=input')
    expect((await stepPanel()).getByText('Not set')).toBeVisible()
  })

  it("shows a package call's values, and its returned values on the Output tab", async () => {
    const call = {
      ...connectorCall(),
      kind: 'package',
      connector: undefined,
      package: 'PaymentReferences',
      version: '1.0.0',
      request: null,
      input: { title: 'Laptops' },
      returned: { reference: 'PAY-9' },
    }
    stubApi({ detail: { status: 200, body: detailForRun({ calls: [call] }) } })

    renderPage('?step=registerPurchase&stepTab=input')

    const panel = await stepPanel()
    expect(panel.getByText('Values')).toBeVisible()
    expect(panel.getByText('Laptops')).toBeVisible()
    await userEvent.click(panel.getByRole('tab', { name: 'Output' }))
    expect(panel.getByText('Returned values')).toBeVisible()
    expect(panel.getByText('PAY-9')).toBeVisible()
  })

  it('shows the answer that settled a connector call on the Output tab, with the body as a table', async () => {
    stubApi({ detail: { status: 200, body: detailForRun({ calls: [connectorCall()] }) } })

    renderPage('?step=registerPurchase&stepTab=output')

    const panel = await stepPanel()
    expect(panel.getByText('Status')).toBeVisible()
    expect(panel.getByText('201')).toBeVisible()
    expect(panel.getByText('content-type')).toBeVisible()
    expect(panel.getByText('PO-77')).toBeVisible()
    expect(panel.queryByText('503')).not.toBeInTheDocument()
  })

  it('shows a changed field as a "−" line and a "+" line on their tints with coloured marks, read as "Changed from X to Y", and a hidden change as Hidden', async () => {
    stubApi({
      detail: {
        status: 200,
        body: detailForRun({
          calls: [],
          made: {
            changes: [
              { field: 'amount', before: 100, after: 120, hidden: false },
              { field: 'note', before: null, after: null, hidden: true },
            ],
          },
        }),
      },
    })

    renderPage('?step=registerPurchase&stepTab=output')

    const panel = await stepPanel()
    const { colour } = designTokensFor('light')
    expect(panel.getByText('amount')).toBeVisible()
    const removed = panel.getByText('100')
    expect(removed).toHaveAttribute('aria-hidden', 'true')
    expect(removed).toHaveStyle({ background: changeTint(colour.danger) })
    expect(within(removed).getByText('−')).toHaveStyle({ color: colour.dangerText })
    const added = panel.getByText('120')
    expect(added).toHaveAttribute('aria-hidden', 'true')
    expect(added).toHaveStyle({ background: changeTint(colour.success) })
    expect(within(added).getByText('+')).toHaveStyle({ color: colour.successText })
    expect(panel.getByText('Changed from 100 to 120')).toBeInTheDocument()
    const note = panel.getByText('note').closest('div')!
    expect(within(note).getByText('Hidden')).toBeVisible()
    expect(within(note).queryByText(/Changed from/)).not.toBeInTheDocument()
  })

  it('names a raised event by its label from the release texts', async () => {
    stubApi({
      detail: { status: 200, body: detailRaisingEvent('events.orderPlaced') },
      releaseTexts: {
        status: 200,
        body: JSON.stringify({ locale: 'en', texts: { 'events.orderPlaced': 'Order placed' } }),
      },
    })

    renderPage('?step=registerPurchase&stepTab=output')

    const panel = await stepPanel()
    expect(panel.getByText('Event raised')).toBeVisible()
    expect(panel.getByText('Order placed')).toBeVisible()
    expect(panel.queryByText('OrderPlaced')).not.toBeInTheDocument()
  })

  it('shows the event a run raised, the people it notified and the record it created', async () => {
    stubApi({
      detail: {
        status: 200,
        body: detailForRun({
          calls: [],
          made: {
            event: { name: 'OrderPlaced', fields: [{ name: 'orderId', value: 'PO-77', hidden: false }] },
            notified: {
              to: [
                { kind: 'user', name: 'Grace Hopper', id: 'grace', hidden: false },
                { kind: 'role', name: 'buyers', id: null, hidden: false },
                { kind: 'email', name: null, id: null, hidden: true },
              ],
              channels: ['inApp', 'email'],
              subject: 'purchaseRequest.submit',
            },
            created: { entity: 'order', id: '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a99' },
          },
        }),
      },
    })

    renderPage('?step=registerPurchase&stepTab=output')

    const panel = await stepPanel()
    expect(panel.getByText('Event raised')).toBeVisible()
    expect(panel.getByText('OrderPlaced')).toBeVisible()
    expect(panel.getByText('PO-77')).toBeVisible()
    expect(panel.getByText('Grace Hopper')).toBeVisible()
    expect(panel.getByText('buyers')).toBeVisible()
    expect(panel.getByText('Hidden')).toBeVisible()
    expect(panel.getByText('In-app and Email')).toBeVisible()
    expect(panel.getByText('Submit for approval')).toBeVisible()
    expect(panel.getByText('Created record')).toBeVisible()
  })

  it("lists a call's attempts newest first and folds a row open to its start, trace id and answer", async () => {
    stubApi({ detail: { status: 200, body: detailForRun({ calls: [connectorCall()] }) } })

    renderPage('?step=registerPurchase&stepTab=attempts')

    const panel = await stepPanel()
    const table = within(panel.getByTestId('inspector-run-attempts'))
    expect(table.getAllByRole('columnheader').map((head) => head.textContent)).toEqual([
      '#',
      'Result',
      'HTTP',
      'Duration',
    ])
    const [newest, oldest] = table.getAllByRole('row').slice(1)
    expect(newest).toHaveTextContent('Succeeded')
    expect(newest).toHaveTextContent('201')
    expect(oldest).toHaveTextContent('Retryable')
    expect(table.queryByText('Trace ID')).not.toBeInTheDocument()

    await userEvent.click(within(newest).getByRole('button', { name: 'Show the details' }))

    expect(table.getByText('Started')).toBeVisible()
    expect(table.getByText('Trace ID')).toBeVisible()
    expect(table.getByText('1bf76519')).toBeVisible()
    expect(table.getByText('PO-77')).toBeVisible()
    await userEvent.click(within(newest).getByRole('button', { name: 'Hide the details' }))
    expect(table.queryByText('Trace ID')).not.toBeVisible()
  })

  it('says "No attempts yet." for a call with no attempts, in place of the table', async () => {
    stubApi({
      detail: { status: 200, body: detailForRun({ calls: [connectorCall({ attempts: [], state: 'pending' })] }) },
    })

    renderPage('?step=registerPurchase&stepTab=attempts')

    const element = await screen.findByTestId('inspector-step-run')
    expect(emptyDescription('No attempts yet.', element)).toBeVisible()
    expect(within(element).queryByTestId('inspector-run-attempts')).not.toBeInTheDocument()
  })

  it('has no Input, Output or Attempts tab for the run of a task, so its panel has no tab bar', async () => {
    stubApi()

    renderPage('?step=managerApproval&stepTab=attempts')

    const panel = await stepPanel()
    expect(panel.getByText('Assigned to')).toBeVisible()
    for (const name of ['Input', 'Output', 'Attempts']) {
      expect(panel.queryByRole('tab', { name })).not.toBeInTheDocument()
    }
    expect(panel.queryByRole('tablist')).not.toBeInTheDocument()
  })
})
