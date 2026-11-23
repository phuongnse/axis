import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { formatDuration, formatValue } from '../platform/formatValue'
import type {
  AuthoringRequest,
  AuthoringRequestDiff,
  AuthoringRequestState,
  AuthoringRequestStep,
  StudioDiagnostic,
  StudioRequest,
  StudioRequestStatus,
  StudioResourceItem,
} from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioAuthoringRequestPage } from './StudioAuthoringRequestPage'
import { requestName } from './studioAuthoringRequest'

const application = 'PurchaseRequests'
const sentAt = '2026-11-18T09:30:00Z'

function request(overrides: Partial<AuthoringRequest> = {}): AuthoringRequest {
  return {
    id: 'r1',
    application,
    author: 'u2',
    authorName: 'Linh Nguyen',
    text: 'Add a supplier field.',
    state: 'planning',
    plan: null,
    round: 0,
    tokens: 0,
    autoAccept: false,
    sentAt,
    approvedAt: null,
    finishedAt: null,
    ...overrides,
  }
}

/** The answer of the read route: the request, or 404 for a request the application does not have. */
function answer(body: AuthoringRequest | null) {
  return body === null
    ? new Response(null, { status: 404 })
    : new Response(JSON.stringify(body), { headers: { 'Content-Type': 'application/json' } })
}

const scenario = {
  name: 'ApprovalBelowThreshold',
  resourceId: 'r1',
  file: 'scenarios/approval.json',
  outcome: 'passed' as const,
  durationMs: 10,
  step: null,
  path: null,
  message: null,
}

/** A `check` step with the checks given, which pass or fail as `outcome` says. */
function checkStep(checks: unknown, outcome: 'passed' | 'failed'): AuthoringRequestStep {
  return {
    kind: 'check',
    summary: null,
    outcome,
    tokens: 0,
    startedAt: sentAt,
    finishedAt: sentAt,
    checks: checks as AuthoringRequestStep['checks'],
  }
}

const passedChecks = {
  compile: { outcome: 'passed', diagnostics: [] },
  packages: { outcome: 'notRun', items: [] },
  scenarios: { outcome: 'passed', request: null, diagnostics: [], items: [scenario] },
}

const problem = (code: string): StudioDiagnostic => ({
  code,
  severity: 'error',
  message: 'The reference has no target.',
  file: 'entities/cost-centre.json',
  path: '/fields/0',
  resourceId: null,
})

const failedCompile = {
  compile: { outcome: 'failed', diagnostics: [problem('AXIS-E101'), problem('AXIS-E102')] },
  packages: { outcome: 'notRun', items: [] },
  scenarios: { outcome: 'notRun', request: null, diagnostics: [], items: [] },
}

/** The changes of the request: an added entity, and a layer that marks one element of a module entity as each mark says. */
const diff: AuthoringRequestDiff = {
  start: 'c0',
  tip: 'c1',
  cursor: null,
  resources: [
    {
      file: 'entities/cost-centre.json',
      change: 'added',
      id: 'e2',
      kind: 'entity',
      name: 'CostCentre',
      before: null,
      after: '{"name":"CostCentre"}',
      target: null,
    },
    {
      file: 'customizations/purchase-request.json',
      change: 'changed',
      id: 'l1',
      kind: null,
      name: null,
      before: '{"target":"modules/purchasing/entities/request.json"}',
      after: '{"target":"modules/purchasing/entities/request.json"}',
      target: 'modules/purchasing/entities/request.json',
      module: {
        id: 'entity-1',
        kind: 'entity',
        name: 'PurchaseRequest',
        file: 'modules/purchasing/entities/request.json',
        before: '{"name":"PurchaseRequest","fields":[{"name":"amount"},{"name":"notes"}]}',
        after: '{"name":"PurchaseRequest","fields":[{"name":"amount","type":"number"},{"name":"supplier"}]}',
        marks: [
          { path: '/fields/1', mark: 'added' },
          { path: '/fields/0', mark: 'changed' },
        ],
        removed: [{ list: '/fields', key: 'notes', content: null }],
      },
    },
  ],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** The accept's publish request, as the accept route and the request read route answer it. */
function acceptRequest(status: StudioRequestStatus, diagnostics: StudioDiagnostic[] = []): StudioRequest {
  return {
    id: 'p1',
    application,
    kind: 'accept',
    target: 'tenant',
    status,
    label: null,
    note: null,
    releaseId: null,
    diagnostics,
    scenarios: null,
  }
}

/**
 * A server that answers by URL and method: a read answers the request as `server.current` holds it, and each action
 * answers with the request in the state it moves to, or with a 500 when `refuse` names it. `calls` lists the actions.
 */
function serverOf(current: AuthoringRequest, refuse: string[] = []) {
  const server = {
    current,
    /** The statuses that reads of the accept's publish request answer in turn, the last one for every read after. */
    accept: ['queued'] as StudioRequestStatus[],
    /** The diagnostics that the accept's publish request answers once it has failed. */
    acceptDiagnostics: [] as StudioDiagnostic[],
    calls: [] as { action: string; body: unknown }[],
    fetch: vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const action = String(input).split('/').pop() ?? ''
      if (init?.method !== 'POST') {
        // The changes of the request answer the diff, in one page.
        if (String(input).endsWith('/diff')) {
          return new Response(JSON.stringify(diff), { headers: { 'Content-Type': 'application/json' } })
        }
        // The accept's publish request. Once it reads succeeded, the request reads accepted, as the publisher moves it.
        if (String(input).endsWith('/requests/p1')) {
          const status = server.accept.length > 1 ? server.accept.shift()! : server.accept[0]
          if (status === 'succeeded') {
            server.current = { ...server.current, state: 'accepted' }
          }
          return json(acceptRequest(status, status === 'failed' ? server.acceptDiagnostics : []))
        }
        return answer(server.current)
      }
      server.calls.push({ action, body: init.body === undefined ? undefined : JSON.parse(String(init.body)) })
      if (refuse.includes(action)) {
        return new Response(null, { status: 500 })
      }
      if (action === 'accept') {
        return json(acceptRequest('queued'), 202)
      }
      const next = { approve: 'working', 'change-plan': 'planning', 'request-changes': 'working', drop: 'dropped' }[
        action
      ] as AuthoringRequestState
      server.current = { ...server.current, state: next }
      return answer(server.current)
    }),
  }
  return server
}

function renderPage(props: { onAskAi?: () => void; userId?: string; resources?: StudioResourceItem[] } = {}) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter>
          <StudioAuthoringRequestPage
            application={application}
            requestId="r1"
            userId={props.userId ?? 'u1'}
            onAskAi={props.onAskAi}
            resources={props.resources}
          />
        </MemoryRouter>
      </TextProvider>
    </ThemeModeProvider>,
  )
}

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('StudioAuthoringRequestPage', () => {
  it('shows the state as it changes, and stops asking once the request waits for its author', async () => {
    vi.useFakeTimers()
    let state: AuthoringRequestState = 'planning'
    const fetchMock = vi.fn(async () => answer(request({ state })))
    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    await act(() => vi.advanceTimersByTimeAsync(0))
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Planning')

    state = 'awaitingApproval'
    await act(() => vi.advanceTimersByTimeAsync(1000))
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Awaiting approval')

    const reads = fetchMock.mock.calls.length
    await act(() => vi.advanceTimersByTimeAsync(3000))
    expect(fetchMock).toHaveBeenCalledTimes(reads)
  })

  it.each([
    ['planning', 'Planning', 'neutral'],
    ['awaitingApproval', 'Awaiting approval', 'attention'],
    ['working', 'Working', 'neutral'],
    ['fixing', 'Working', 'neutral'],
    ['checking', 'Checking', 'neutral'],
    ['reviewing', 'Reviewing', 'neutral'],
    ['ready', 'Ready', 'attention'],
    ['accepted', 'Accepted', 'success'],
    ['dropped', 'Dropped', 'muted'],
    ['failed', 'Failed', 'danger'],
  ] as const)('shows %s as "%s" in the %s tone', async (state, word, tone) => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => answer(request({ state }))),
    )
    renderPage()

    const tag = await screen.findByTestId('studio-ai-request-state')
    expect(tag.textContent).toBe(word)
    expect(tag).toHaveAttribute('data-tone', tone)
  })

  it('shows that the request does not exist, with an Ask AI link that opens the request panel', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => answer(null)),
    )
    const onAskAi = vi.fn()
    renderPage({ onAskAi })

    expect(await screen.findByText('This request does not exist.')).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Ask AI' }))
    expect(onAskAi).toHaveBeenCalledTimes(1)
  })

  it('shows the name cut at 80 characters, the words in full and the sender line of another person', async () => {
    const sentence =
      'Add a supplier field to the purchase request form so that every request names the supplier it comes from'
    const text = `${sentence}. It must be required for every new request.`
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => answer(request({ text }))),
    )
    renderPage()

    const name = `${sentence.slice(0, 80).trimEnd()}…`
    expect(name).toHaveLength(81)
    expect(await screen.findByRole('heading', { level: 1, name })).toBeVisible()
    expect(screen.getByText(text)).toBeVisible()
    const time = formatValue({ name: 'sentAt', type: 'date-time' }, sentAt, {}, { locale: 'en', text: (key) => key })
    expect(screen.getByText(`Sent by Linh Nguyen on ${time}`)).toBeVisible()
  })

  /** Answers `/api/me` with the signed-in person, and every other read with the request. */
  function stubAs(me: { id: string; users: boolean }, body = request({ author: 'u1' })) {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => (input === '/api/me' ? new Response(JSON.stringify(me)) : answer(body))),
    )
  }
  const sentTime = () =>
    formatValue({ name: 'sentAt', type: 'date-time' }, sentAt, {}, { locale: 'en', text: (key) => key })

  it("names the author in the sender's line, followed by (you), when the request is the reader's own", async () => {
    stubAs({ id: 'u1', users: false })
    renderPage()

    const you = await screen.findByText('(you)')
    expect(you.parentElement).toHaveTextContent(`Sent by Linh Nguyen (you) on ${sentTime()}`)
    expect(screen.queryByText(/Sent by you/)).not.toBeInTheDocument()
  })

  it("links the author's name to the user page for a reader who may administer users", async () => {
    stubAs({ id: 'u9', users: true }, request({ author: 'u1' }))
    renderPage({ userId: 'u9' })

    expect(await screen.findByRole('link', { name: 'Linh Nguyen' })).toHaveAttribute('href', '/users/u1')
    expect(screen.queryByText('(you)')).not.toBeInTheDocument()
  })
})

describe('the plan and the guide', () => {
  const plan = 'Add a supplier text field to the Request entity.\nShow it on the form.'
  const waiting = () => request({ author: 'u1', state: 'awaitingApproval', plan })
  const button = (name: string) => screen.queryByRole('button', { name })

  it('shows its author the plan, where the request stands and the three actions', async () => {
    vi.stubGlobal('fetch', serverOf(waiting()).fetch)
    renderPage()

    expect(await screen.findByText(/Add a supplier text field to the Request entity\./)).toBeVisible()
    const guide = screen.getByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('Plan ready for your approval')).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Approve plan' })).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Change the plan…' })).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Drop…' })).toBeVisible()
  })

  it('shows another reader the plan and who it waits for, with no actions', async () => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u2', state: 'awaitingApproval', plan })).fetch)
    renderPage()

    expect(await screen.findByText(/Add a supplier text field to the Request entity\./)).toBeVisible()
    expect(screen.getByText("Waiting for its author's approval")).toBeVisible()
    expect(button('Approve plan')).toBeNull()
    expect(button('Change the plan…')).toBeNull()
    expect(button('Drop…')).toBeNull()
  })

  it('shows that AI writes the plan, with Drop… for its author only', async () => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u1', state: 'planning' })).fetch)
    renderPage()

    expect(
      await screen.findByText(
        'AI is reading the application and writing a plan. Nothing changes until the plan is approved.',
      ),
    ).toBeVisible()
    expect(screen.getByText('AI is writing the plan')).toBeVisible()
    expect(button('Drop…')).not.toBeNull()
    expect(button('Approve plan')).toBeNull()
    expect(button('Change the plan…')).toBeNull()
  })

  it.each([
    ['working', 'AI is working on it', true],
    ['accepted', 'Accepted. Live once published', false],
    ['dropped', 'Dropped. Nothing changed', false],
  ] as const)('says "%s" in the guide as "%s", with Drop… only while AI works', async (state, words, drop) => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u1', state, plan })).fetch)
    renderPage()

    expect(await screen.findByText(words)).toBeVisible()
    expect(button('Drop…') !== null).toBe(drop)
  })

  it('draws no guide yet for a failed request', async () => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u1', state: 'failed', plan })).fetch)
    renderPage()

    await screen.findByTestId('studio-ai-request-state')
    expect(screen.queryByTestId('studio-ai-request-guide')).toBeNull()
  })

  it('approves the plan and shows the request as Working', async () => {
    const server = serverOf(waiting())
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Approve plan' }))

    expect(await screen.findByText('AI is working on it')).toBeVisible()
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Working')
    expect(server.calls).toEqual([{ action: 'approve', body: undefined }])
    expect(screen.queryByTestId('studio-ai-request-error')).toBeNull()
  })

  it('shows "That did not work. Try again." above the plan when Approve plan is refused', async () => {
    const server = serverOf(waiting(), ['approve'])
    vi.stubGlobal('fetch', server.fetch)
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Approve plan' }))

    const alert = await screen.findByTestId('studio-ai-request-error')
    expect(alert).toHaveTextContent('That did not work. Try again.')
    const planPart = screen.getByTestId('studio-ai-request-plan')
    expect(alert.compareDocumentPosition(planPart) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Awaiting approval')
  })

  it('asks for a new plan with a comment and shows the request as Planning', async () => {
    const server = serverOf(waiting())
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Change the plan…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Change the plan' })
    await userEvent.type(
      within(dialog).getByRole('textbox', { name: 'What should the plan do differently?' }),
      'Use a list',
    )
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ask for a new plan' }))

    expect(await screen.findByText('AI is writing the plan')).toBeVisible()
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Planning')
    expect(server.calls).toEqual([{ action: 'change-plan', body: { comment: 'Use a list' } }])
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('asks for the comment before it sends a change of plan', async () => {
    const server = serverOf(waiting())
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Change the plan…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Change the plan' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ask for a new plan' }))

    expect(await within(dialog).findByText('Write what the plan should do differently.')).toBeInTheDocument()
    expect(server.calls).toEqual([])
  })

  it('shows "That did not work. Try again." inside the dialog when a change of plan is refused', async () => {
    const server = serverOf(waiting(), ['change-plan'])
    vi.stubGlobal('fetch', server.fetch)
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Change the plan…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Change the plan' })
    await userEvent.type(within(dialog).getByRole('textbox'), 'Use a list')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ask for a new plan' }))

    expect(await within(dialog).findByText('That did not work. Try again.')).toBeVisible()
    expect(screen.queryByTestId('studio-ai-request-error')).toBeNull()
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Awaiting approval')
  })

  it('drops the request after a confirmation and shows it as Dropped', async () => {
    const server = serverOf(waiting())
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Drop…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Drop this request?' })
    expect(within(dialog).getByText('AI stops working on this request, and its draft is deleted.')).toBeVisible()
    expect(server.calls).toEqual([])
    await userEvent.click(within(dialog).getByRole('button', { name: 'Drop' }))

    expect(await screen.findByText('Dropped. Nothing changed')).toBeVisible()
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Dropped')
    expect(server.calls).toEqual([{ action: 'drop', body: undefined }])
  })

  it('shows "That did not work. Try again." when a drop is refused', async () => {
    const server = serverOf(waiting(), ['drop'])
    vi.stubGlobal('fetch', server.fetch)
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Drop…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Drop this request?' })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Drop' }))

    expect(await screen.findByTestId('studio-ai-request-error')).toHaveTextContent('That did not work. Try again.')
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Awaiting approval')
  })
})

describe('the feed', () => {
  const plan = 'Add a supplier field.'
  const approvedAt = '2026-11-18T09:05:00Z'
  const approved = { author: 'u1', plan, approvedAt }
  const ready = checkStep(passedChecks, 'passed')

  const cases: [string, Partial<AuthoringRequest>, [string, string][]][] = [
    ['planning', { state: 'planning' }, [['Plan', 'In progress']]],
    ['awaitingApproval', { author: 'u1', state: 'awaitingApproval', plan }, [['Plan', 'Waiting']]],
    [
      'working',
      { ...approved, state: 'working' },
      [
        ['Plan', 'Done'],
        ['Work', 'In progress'],
      ],
    ],
    [
      'ready',
      { ...approved, state: 'ready', steps: [ready] },
      [
        ['Plan', 'Done'],
        ['Work', 'Done'],
        ['Result', 'Waiting'],
      ],
    ],
    [
      'accepted',
      { ...approved, state: 'accepted', steps: [ready] },
      [
        ['Plan', 'Done'],
        ['Work', 'Done'],
        ['Result', 'Done'],
      ],
    ],
    [
      'failed after approval',
      { ...approved, state: 'failed', steps: [checkStep(failedCompile, 'failed')] },
      [
        ['Plan', 'Done'],
        ['Work', 'Stopped'],
        ['Result', 'Not reached'],
      ],
    ],
    ['failed before approval', { author: 'u1', state: 'failed' }, [['Plan', 'Stopped']]],
    [
      'dropped before approval',
      { author: 'u1', state: 'dropped', plan },
      [
        ['Plan', 'Not reached'],
        ['Dropped', 'Not reached'],
      ],
    ],
    [
      'dropped after approval',
      { ...approved, state: 'dropped', finishedAt: '2026-11-18T10:00:00Z' },
      [
        ['Plan', 'Done'],
        ['Work', 'Not reached'],
        ['Dropped', 'Not reached'],
      ],
    ],
  ]

  it.each(cases)('reads a %s request as one feed with the marks of its items', async (_, overrides, expected) => {
    vi.stubGlobal('fetch', serverOf(request(overrides)).fetch)
    renderPage()

    const bar = await screen.findByTestId('steps-bar')
    const items = Array.from(bar.children) as HTMLElement[]
    expect(
      items.map(
        (item) => within(item).getAllByRole('img', { name: /^(Done|In progress|Waiting|Stopped|Not reached)$/ }).length,
      ),
    ).toEqual(expected.map(() => 1))
    expected.forEach(([title, mark], index) => {
      expect(within(items[index]).getByText(title, { exact: true })).toBeVisible()
      expect(within(items[index]).getByRole('img', { name: mark })).toBeVisible()
    })
    expect(items).toHaveLength(expected.length)
    expect(screen.getAllByTestId('steps-bar')).toHaveLength(1)
    expect(screen.getByTestId('studio-ai-request-feed')).toContainElement(bar)
    expect(screen.queryByRole('tablist')).toBeNull()
  })

  it('shows when a dropped request was dropped, as the standing of its Dropped item', async () => {
    const finishedAt = '2026-11-18T10:00:00Z'
    vi.stubGlobal('fetch', serverOf(request({ state: 'dropped', finishedAt, plan })).fetch)
    renderPage()

    const items = Array.from((await screen.findByTestId('steps-bar')).children) as HTMLElement[]
    const time = formatValue(
      { name: 'finishedAt', type: 'date-time' },
      finishedAt,
      {},
      { locale: 'en', text: (key) => key },
    )
    expect(within(items[1]).getByText(time)).toBeVisible()
  })

  it('shows the failure of a request that failed after its plan was approved inside the Work item, as one alert line', async () => {
    const steps = [checkStep(failedCompile, 'failed')]
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'failed', steps })).fetch)
    renderPage()

    const items = Array.from((await screen.findByTestId('steps-bar')).children) as HTMLElement[]
    expect(within(items[0]).queryByTestId('studio-ai-request-failure')).toBeNull()
    expect(within(items[1]).getByTestId('studio-ai-request-failure')).toBeVisible()
    expect(screen.getAllByTestId('studio-ai-request-failure')).toHaveLength(1)
  })

  it('shows the failure of a request that failed before its plan was approved inside the Plan item', async () => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u1', state: 'failed' })).fetch)
    renderPage()

    const items = Array.from((await screen.findByTestId('steps-bar')).children) as HTMLElement[]
    expect(items).toHaveLength(1)
    expect(within(items[0]).getByTestId('studio-ai-request-failure')).toHaveTextContent(
      'AI could not finish this request. A step did not finish.',
    )
  })

  it('shows the accept the page follows, and its conflict, at the end of the Result item', async () => {
    const server = serverOf(request({ ...approved, state: 'ready', steps: [ready] }))
    server.accept = ['failed']
    server.acceptDiagnostics = [{ ...problem('AXC0377'), message: 'Changed.', resourceId: 'e2' }]
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Accept' }))

    const conflict = await screen.findByTestId('studio-ai-request-conflict')
    const items = Array.from(screen.getByTestId('steps-bar').children) as HTMLElement[]
    expect(items[2]).toContainElement(conflict)
  })
})

describe('the Work item', () => {
  const approvedAt = '2026-11-18T09:05:00Z'
  const approved = { author: 'u1', plan: 'Add a supplier field.', approvedAt }
  const work = async () => {
    const items = Array.from((await screen.findByTestId('steps-bar')).children) as HTMLElement[]
    return items[1]
  }

  it.each([
    ['working', 'Changing the draft'],
    ['fixing', 'Fixing the draft'],
    ['checking', 'Checking the draft'],
    ['reviewing', 'Reviewing the draft'],
  ] as const)('reads what runs now while the request is %s', async (state, line) => {
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state })).fetch)
    renderPage()

    expect(within(await work()).getByText(line)).toBeVisible()
  })

  it('reads the count of its steps and their duration once the request is ready, and nothing beside its title', async () => {
    const timed = (kind: string, from: string, to: string): AuthoringRequestStep => ({
      ...checkStep(passedChecks, 'passed'),
      kind,
      checks: kind === 'check' ? (passedChecks as unknown as AuthoringRequestStep['checks']) : null,
      startedAt: `2026-11-18T${from}Z`,
      finishedAt: `2026-11-18T${to}Z`,
    })
    const steps = [
      timed('work', '09:06:00', '09:07:00'),
      timed('check', '09:07:00', '09:08:00'),
      timed('review', '09:08:00', '09:09:30'),
    ]
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'ready', steps })).fetch)
    renderPage()

    const item = await work()
    const duration = formatDuration(210_000, 'en', (key) => platformEnglish[key as keyof typeof platformEnglish])
    expect(within(item).getByText(`3 steps in ${duration}`)).toBeVisible()
    expect(within(item).queryByText(/Fix round/)).toBeNull()
  })

  it('reads "Fix round 2" beside its title while AI fixes', async () => {
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'fixing', round: 2 })).fetch)
    renderPage()

    const item = await work()
    const standing = within(item).getByText('Fix round 2')
    expect(standing).toBeVisible()
    expect(within(item).getByText('Work').closest('.ant-typography')?.parentElement).toContainElement(standing)
  })
})

describe('the Plan item of an approved plan', () => {
  const approvedAt = '2026-11-18T09:05:00Z'
  const plan = 'Add a supplier field.\n\nShow it on the form.'
  const asked = (comment: string): AuthoringRequestStep => ({
    kind: 'planChange',
    summary: comment,
    outcome: null,
    tokens: 0,
    startedAt: sentAt,
    finishedAt: sentAt,
    checks: null,
  })
  const planItem = async () => (Array.from((await screen.findByTestId('steps-bar')).children) as HTMLElement[])[0]

  it('reads "Approved on <time>", folds the plan under its first paragraph and shows the rest with Show the plan', async () => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u1', state: 'working', plan, approvedAt })).fetch)
    renderPage()

    const item = await planItem()
    const time = formatValue(
      { name: 'approvedAt', type: 'date-time' },
      approvedAt,
      {},
      { locale: 'en', text: (key) => key },
    )
    expect(within(item).getByText(`Approved on ${time}`)).toBeVisible()
    expect(within(item).getByText('Add a supplier field.')).toBeVisible()
    expect(within(item).queryByText('Show it on the form.')).toBeNull()
    const show = within(item).getByRole('button', { name: 'Show the plan' })
    expect(show).toHaveAttribute('aria-expanded', 'false')

    await userEvent.click(show)

    expect(within(item).getByText('Show it on the form.')).toBeVisible()
    const hide = within(item).getByRole('button', { name: 'Hide the plan' })
    expect(hide).toHaveAttribute('aria-expanded', 'true')
    await userEvent.click(hide)
    expect(within(item).queryByText('Show it on the form.')).toBeNull()
  })

  it('shows the comments that asked for another plan first, oldest first, as words to read', async () => {
    const steps = [asked('Add the cost centre.'), asked('Also add it to the form.')]
    vi.stubGlobal('fetch', serverOf(request({ author: 'u1', state: 'working', plan, approvedAt, steps })).fetch)
    renderPage()

    const item = await planItem()
    const comments = within(item).getAllByText(/^Asked for another plan: /)
    expect(comments.map((comment) => comment.textContent)).toEqual([
      'Asked for another plan: “Add the cost centre.”',
      'Asked for another plan: “Also add it to the form.”',
    ])
    const summary = within(item).getByText('Add a supplier field.')
    for (const comment of comments) {
      expect(comment.compareDocumentPosition(summary) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    }
  })

  it('shows the whole plan open while it waits for its author, with no standing beside its title', async () => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u1', state: 'awaitingApproval', plan })).fetch)
    renderPage()

    const item = await planItem()
    expect(within(item).getByText('Show it on the form.')).toBeVisible()
    expect(within(item).getByRole('button', { name: 'Hide the plan' })).toHaveAttribute('aria-expanded', 'true')
    expect(within(item).queryByText(/^Approved on/)).toBeNull()
  })

  it('shows no Show the plan for a plan of one paragraph', async () => {
    vi.stubGlobal(
      'fetch',
      serverOf(request({ author: 'u1', state: 'working', plan: 'Add a field.', approvedAt })).fetch,
    )
    renderPage()

    const item = await planItem()
    expect(within(item).getByText('Add a field.')).toBeVisible()
    expect(within(item).queryByRole('button')).toBeNull()
  })
})

describe('requestName', () => {
  it('takes the first sentence and keeps its mark', () => {
    expect(requestName('  Add a field. Then test it!  ')).toBe('Add a field.')
    expect(requestName('Why now?')).toBe('Why now?')
  })

  it('does not end a sentence at a point inside a word', () => {
    expect(requestName('Upgrade to v1.2 now. Then test')).toBe('Upgrade to v1.2 now.')
  })

  it('takes the first line when no sentence ends before it', () => {
    expect(requestName('Add a field\nAlso check the totals')).toBe('Add a field')
    expect(requestName('Add a field\nAlso check the totals.')).toBe('Add a field')
  })

  it('cuts a name longer than 80 characters with an ellipsis', () => {
    const long = 'a'.repeat(100)

    expect(requestName(long)).toBe(`${'a'.repeat(80)}…`)
  })
})

describe('StudioAuthoringRequestPage Result item', () => {
  it('shows a ready request with its checks passed and its review not run, to its author', async () => {
    const server = serverOf(request({ state: 'ready', author: 'u1', steps: [checkStep(passedChecks, 'passed')] }))
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ userId: 'u1' })

    const checks = await screen.findByTestId('studio-ai-request-checks')
    expect(within(checks).getByText('Checks')).toBeVisible()
    expect(within(checks).getByText('Passed')).toBeVisible()
    expect(within(checks).getByText('All scenarios pass')).toBeVisible()
    const review = screen.getByTestId('studio-ai-request-review')
    expect(within(review).getByText('Not reviewed')).toBeVisible()
    expect(within(review).getByText('Connect your AI to have a second AI review changes.')).toBeVisible()
  })

  it('tells another author that no second AI reviewed the change, with no actions', async () => {
    const server = serverOf(request({ state: 'ready', author: 'u1', steps: [checkStep(passedChecks, 'passed')] }))
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ userId: 'u9' })

    const review = await screen.findByTestId('studio-ai-request-review')
    expect(within(review).getByText('Not reviewed')).toBeVisible()
    expect(
      within(review).getByText('No second AI reviewed this change, as its author has not connected their AI.'),
    ).toBeVisible()
    expect(screen.queryByRole('button', { name: 'Drop…' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve plan' })).not.toBeInTheDocument()
  })

  it('lists each resource of the diff folded, and names a layer by its module resource with its marks', async () => {
    const server = serverOf(request({ state: 'ready', author: 'u1', steps: [checkStep(passedChecks, 'passed')] }))
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ userId: 'u1' })

    const files = await screen.findAllByTestId('studio-changes-file')
    expect(files).toHaveLength(2)
    expect(screen.queryByTestId('json-diff-after')).not.toBeInTheDocument()

    const layer = files.find((file) => file.dataset.file === 'customizations/purchase-request.json')!
    expect(within(layer).getByText('PurchaseRequest')).toBeVisible()
    expect(within(layer).getByRole('note', { name: 'supplier, added by this application' })).toHaveTextContent('Added')
    expect(within(layer).getByRole('note', { name: 'amount, changed by this application' })).toHaveTextContent(
      'Changed',
    )
    expect(within(layer).getByRole('note', { name: 'notes, removed by this application' })).toHaveTextContent('Removed')
    expect(within(layer).queryByRole('button', { name: /Reset/ })).not.toBeInTheDocument()

    await userEvent.click(within(layer).getByRole('button', { name: 'Show the diff of PurchaseRequest' }))
    expect(within(layer).getByTestId('json-diff-after')).toHaveTextContent('"supplier"')
  })

  it('shows a failed request with its reason, its failed checks with both problems, and no review line', async () => {
    const server = serverOf(
      request({ state: 'failed', author: 'u1', round: 1, steps: [checkStep(failedCompile, 'failed')] }),
    )
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ userId: 'u1' })

    const alert = await screen.findByTestId('studio-ai-request-failure')
    expect(alert).toHaveTextContent(
      'AI could not finish this request. The checks still failed after the last fix round.',
    )
    expect(alert.querySelector('.ant-alert-description')).toBeNull()

    const checks = screen.getByTestId('studio-ai-request-checks')
    expect(within(checks).getByText('Failed')).toBeVisible()
    expect(within(checks).getByText('It does not compile: 2 problems')).toBeVisible()
    expect(screen.queryByTestId('studio-ai-request-review')).not.toBeInTheDocument()

    await userEvent.click(within(checks).getByRole('button', { name: 'Show the checks' }))
    const detail = screen.getByTestId('studio-ai-request-checks-detail')
    expect(within(detail).getByText('AXIS-E101')).toBeVisible()
    expect(within(detail).getByText('AXIS-E102')).toBeVisible()
  })
})

describe('accepting a ready request', () => {
  const ready = (overrides: Partial<AuthoringRequest> = {}) =>
    request({
      state: 'ready',
      author: 'u1',
      plan: 'Add a field.',
      steps: [checkStep(passedChecks, 'passed')],
      ...overrides,
    })
  const accept = () => screen.findByRole('button', { name: 'Accept' })
  /** An `AXC0377` of an accept: the resource's id or `null` for a package file, its file, and who changed it and when. */
  const conflict = (
    resourceId: string | null,
    file: string,
    changedBy: string | null,
    changedAt: string | null,
  ): StudioDiagnostic => ({
    code: 'AXC0377',
    severity: 'error',
    message: 'The resource changed on main since the draft read it.',
    file,
    path: '',
    resourceId,
    changedBy,
    changedAt,
  })
  const employee: StudioResourceItem = {
    id: 'role-1',
    kind: 'role',
    name: 'employee',
    file: 'roles/employee.json',
    version: 'v1',
    usedBy: 0,
    titles: { en: 'Employee' },
  }
  const timeOf = (value: string) =>
    formatValue({ name: 'changedAt', type: 'date-time' }, value, {}, { locale: 'en', text: (key) => key })

  it('gives its author Accept and Drop… with the words "Checks and review passed"', async () => {
    vi.stubGlobal('fetch', serverOf(ready()).fetch)
    renderPage()

    const guide = await screen.findByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('Checks and review passed')).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Accept' })).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Drop…' })).toBeVisible()
  })

  it('tells another reader that it waits for its author, with no buttons', async () => {
    vi.stubGlobal('fetch', serverOf(ready({ author: 'u2' })).fetch)
    renderPage()

    const guide = await screen.findByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('Waiting for its author to accept')).toBeVisible()
    expect(within(guide).queryByRole('button')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull()
  })

  it('asks for other changes with a comment of 2,000 characters and shows the request as working', async () => {
    const server = serverOf(ready())
    vi.stubGlobal('fetch', server.fetch)
    renderPage()
    const comment = 'a'.repeat(2000)

    await userEvent.click(await screen.findByRole('button', { name: 'Ask for other changes…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Ask for other changes' })
    expect(within(dialog).getByText(/AI works on the same draft with your comment/)).toBeVisible()
    await userEvent.click(within(dialog).getByRole('textbox', { name: 'What should AI change?' }))
    await userEvent.paste(comment)
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ask AI to change it' }))

    expect(await within(screen.getByTestId('studio-ai-request-guide')).findByText('AI is working on it')).toBeVisible()
    expect(server.calls).toEqual([{ action: 'request-changes', body: { comment } }])
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('shows another reader no way to ask for other changes', async () => {
    vi.stubGlobal('fetch', serverOf(ready({ author: 'u2' })).fetch)
    renderPage()

    await screen.findByTestId('studio-ai-request-guide')
    expect(screen.queryByRole('button', { name: 'Ask for other changes…' })).toBeNull()
  })

  it('shows "Accepting the draft" with no buttons while the accept is queued', async () => {
    const server = serverOf(ready())
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await accept())

    const guide = screen.getByTestId('studio-ai-request-guide')
    expect(await within(guide).findByText('Accepting the draft')).toBeVisible()
    expect(within(guide).queryByRole('button')).toBeNull()
    expect(await screen.findByTestId('studio-request-status')).toHaveAttribute('data-status', 'queued')
    expect(server.calls).toEqual([{ action: 'accept', body: undefined }])
  })

  it('shows "Not accepted" when the accept fails, keeps the request Ready and shows Accept again', async () => {
    const server = serverOf(ready())
    server.accept = ['queued', 'failed']
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await accept())

    const status = await screen.findByText('Not accepted. Nothing in the application changed.', undefined, {
      timeout: 4000,
    })
    expect(status).toBeVisible()
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Ready')
    expect(await accept()).toBeVisible()
    expect(screen.getByText('Checks and review passed')).toBeVisible()
  })

  it('shows "Not accepted" with no conflict alert when the failed accept has no conflict', async () => {
    const server = serverOf(ready())
    server.accept = ['queued', 'failed']
    server.acceptDiagnostics = [problem('AXIS-E101')]
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await accept())

    expect(
      await screen.findByText('Not accepted. Nothing in the application changed.', undefined, { timeout: 4000 }),
    ).toBeVisible()
    expect(screen.queryByTestId('studio-ai-request-conflict')).toBeNull()
    expect(await accept()).toBeVisible()
  })

  it('shows a conflict alert with a bullet for each changed resource, and Drop… with no Accept', async () => {
    const server = serverOf(ready())
    server.accept = ['queued', 'failed']
    server.acceptDiagnostics = [
      conflict('role-1', 'roles/employee.json', 'Linh Nguyen', '2026-11-18T10:15:00Z'),
      conflict(null, 'packages/tax/rates.json', 'Minh Tran', '2026-11-18T11:40:00Z'),
    ]
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ resources: [employee] })

    await userEvent.click(await accept())

    const alert = await screen.findByTestId('studio-ai-request-conflict', undefined, { timeout: 4000 })
    expect(
      within(alert).getByText('Someone changed these resources after AI read them, so nothing was accepted:'),
    ).toBeVisible()
    const items = within(alert).getAllByTestId('studio-ai-request-conflict-item')
    expect(items).toHaveLength(2)
    expect(items[0].textContent).toBe(`Employee, changed by Linh Nguyen on ${timeOf('2026-11-18T10:15:00Z')}`)
    expect(within(items[0]).getByText(timeOf('2026-11-18T10:15:00Z'), { selector: 'time' })).toHaveAttribute(
      'datetime',
      '2026-11-18T10:15:00Z',
    )
    expect(items[1].textContent).toBe(
      `packages/tax/rates.json, changed by Minh Tran on ${timeOf('2026-11-18T11:40:00Z')}`,
    )

    const guide = screen.getByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('Resources changed since AI read them')).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Drop…' })).toBeVisible()
    expect(within(guide).queryByRole('button', { name: 'Accept' })).toBeNull()
    expect(screen.queryByText('Not accepted. Nothing in the application changed.')).toBeNull()
    expect(screen.queryByTestId('studio-request-status')).toBeNull()
  })

  it('shows "Accepted" and the state Accepted when the accept succeeds', async () => {
    const server = serverOf(ready())
    server.accept = ['queued', 'succeeded']
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await accept())

    expect(
      await screen.findByText(
        'Accepted. The change is in the application and in History. It is not live until someone publishes.',
        undefined,
        { timeout: 4000 },
      ),
    ).toBeVisible()
    await waitFor(() => expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Accepted'))
    expect(screen.queryByRole('button', { name: 'Accept' })).toBeNull()
  })

  it('shows "That did not work. Try again." when the accept is refused', async () => {
    const server = serverOf(ready(), ['accept'])
    vi.stubGlobal('fetch', server.fetch)
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    renderPage()

    await userEvent.click(await accept())

    expect(await screen.findByTestId('studio-ai-request-error')).toHaveTextContent('That did not work. Try again.')
    expect(screen.queryByTestId('studio-request-status')).toBeNull()
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Ready')
    expect(await accept()).toBeVisible()
  })
})
