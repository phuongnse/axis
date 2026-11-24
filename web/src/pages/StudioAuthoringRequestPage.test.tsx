import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { formatDuration, formatValue } from '../platform/formatValue'
import type {
  AuthoringPlan,
  AuthoringRequest,
  AuthoringRequestDiff,
  AuthoringRequestState,
  AuthoringRequestStep,
  StudioDiagnostic,
  StudioRequest,
  StudioRequestStatus,
  StudioResourceItem,
} from '../platform/studio'
import { studioResourceAddress } from '../platform/studioAddress'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioAuthoringRequestPage } from './StudioAuthoringRequestPage'
import { requestName } from './studioAuthoringRequest'

const application = 'PurchaseRequests'
const sentAt = '2026-11-18T09:30:00Z'

/** A plan of the plan's shape: its summary and changes, and every other group empty. */
function planOf(summary: string, changes: string[] = []): AuthoringPlan {
  return { summary, questions: [], changes, decisions: [], risks: [], checks: [], checklist: [], details: [] }
}

function request(overrides: Partial<AuthoringRequest> = {}): AuthoringRequest {
  return {
    id: 'r1',
    application,
    author: 'u2',
    authorName: 'Linh Nguyen',
    text: 'Add a supplier field.',
    title: null,
    state: 'planning',
    plan: null,
    round: 0,
    tokens: 0,
    autoAccept: false,
    sentAt,
    approvedAt: null,
    finishedAt: null,
    draft: null,
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
    resource: null,
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

/** The preview of the draft, as the draft preview route and the request read route answer it. */
function previewRequest(status: StudioRequestStatus): StudioRequest {
  return { ...acceptRequest(status), id: 'p2', kind: 'publish', target: 'preview' }
}

const siteId = 'site-1'

/** The application's site, which the preview links name. */
const site: StudioResourceItem = {
  id: siteId,
  kind: 'site',
  name: 'Purchasing',
  file: 'sites/purchasing.json',
  version: 'a',
  usedBy: 0,
  titles: { en: 'Purchasing' },
}

/**
 * A server that answers by URL and method: a read answers the request as `server.current` holds it, and each action
 * answers with the request in the state it moves to, or with a 500 when `refuse` names it. `calls` lists the actions. The
 * diff answers `changed`, which is `diff` unless a test gives its own.
 */
function serverOf(current: AuthoringRequest, refuse: string[] = [], changed: AuthoringRequestDiff = diff) {
  const server = {
    current,
    /** The statuses that reads of the accept's publish request answer in turn, the last one for every read after. */
    accept: ['queued'] as StudioRequestStatus[],
    /** The statuses that reads of the draft's preview answer in turn, the last one for every read after. */
    preview: ['succeeded'] as StudioRequestStatus[],
    /** The diagnostics that the accept's publish request answers once it has failed. */
    acceptDiagnostics: [] as StudioDiagnostic[],
    calls: [] as { action: string; body: unknown }[],
    fetch: vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const action = String(input).split('/').pop() ?? ''
      if (init?.method !== 'POST') {
        // The changes of the request answer the diff, in one page.
        if (String(input).endsWith('/diff')) {
          return new Response(JSON.stringify(changed), { headers: { 'Content-Type': 'application/json' } })
        }
        // The accept's publish request. Once it reads succeeded, the request reads accepted, as the publisher moves it.
        if (String(input).endsWith('/requests/p1')) {
          const status = server.accept.length > 1 ? server.accept.shift()! : server.accept[0]
          if (status === 'succeeded') {
            server.current = { ...server.current, state: 'accepted' }
          }
          return json(acceptRequest(status, status === 'failed' ? server.acceptDiagnostics : []))
        }
        if (String(input).endsWith('/requests/p2')) {
          return json(previewRequest(server.preview.length > 1 ? server.preview.shift()! : server.preview[0]))
        }
        if (String(input).endsWith(`/resources/${siteId}`)) {
          return json({ ...site, content: { path: 'purchasing' } })
        }
        return answer(server.current)
      }
      server.calls.push({ action, body: init.body === undefined ? undefined : JSON.parse(String(init.body)) })
      if (refuse.includes(action)) {
        return new Response(null, { status: 500 })
      }
      // Send again posts a new request, which the server answers in planning.
      if (action === 'authoring-requests') {
        return json({ ...server.current, id: 'r2', state: 'planning' }, 201)
      }
      if (action === 'accept') {
        return json(acceptRequest('queued'), 202)
      }
      if (action === 'preview') {
        return json(previewRequest('queued'), 202)
      }
      const next = {
        approve: 'working',
        'change-plan': 'planning',
        'request-changes': 'working',
        retry: 'working',
        drop: 'dropped',
      }[action] as AuthoringRequestState
      server.current = { ...server.current, state: next }
      return answer(server.current)
    }),
  }
  return server
}

/** Shows the address the router is at, so a test can read where an action opened. */
function Location() {
  const location = useLocation()
  return <output data-testid="location">{location.pathname + location.search}</output>
}

function renderPage(
  props: { onAskAi?: () => void; userId?: string; resources?: StudioResourceItem[]; previewHost?: string | null } = {},
) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter initialEntries={[`/studio?app=${application}&view=requests&request=r1`]}>
          <StudioAuthoringRequestPage
            application={application}
            requestId="r1"
            userId={props.userId ?? 'u1'}
            onAskAi={props.onAskAi}
            resources={props.resources}
            previewHost={props.previewHost ?? null}
          />
          <Location />
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

  it("shows AI's title as the heading once AI has set one, in place of the first sentence of the words", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () =>
        answer(request({ title: 'Add a supplier', state: 'awaitingApproval', plan: planOf('Adds a supplier field.') })),
      ),
    )
    renderPage()

    expect(await screen.findByRole('heading', { level: 1, name: 'Add a supplier' })).toBeVisible()
    expect(screen.queryByRole('heading', { level: 1, name: 'Add a supplier field.' })).not.toBeInTheDocument()
    expect(screen.getByText('Add a supplier field.')).toBeVisible()
  })

  it('shows the first sentence of the words as the heading while the title is null', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => answer(request({ title: null, text: 'Add a supplier field. It must be required.' }))),
    )
    renderPage()

    expect(await screen.findByRole('heading', { level: 1, name: 'Add a supplier field.' })).toBeVisible()
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
  const plan = planOf('Add a supplier text field to the Request entity.', [
    'Add a Supplier field.',
    'Show it on the form.',
  ])
  const waiting = () => request({ author: 'u1', state: 'awaitingApproval', plan })
  const button = (name: string) => screen.queryByRole('button', { name })

  it('shows its author the plan, where the request stands and the three actions', async () => {
    vi.stubGlobal('fetch', serverOf(waiting()).fetch)
    renderPage()

    expect(await screen.findByText('Add a supplier text field to the Request entity.')).toBeVisible()
    const guide = screen.getByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('Plan ready for your approval')).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Approve plan' })).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Change the plan…' })).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Drop…' })).toBeVisible()
  })

  it("shows the plan's summary, then each change as a bullet", async () => {
    vi.stubGlobal('fetch', serverOf(waiting()).fetch)
    renderPage()

    const part = await screen.findByTestId('studio-ai-request-plan')
    const summary = within(part).getByText('Add a supplier text field to the Request entity.')
    const items = within(part).getAllByRole('listitem')
    expect(items.map((item) => item.textContent)).toEqual(['Add a Supplier field.', 'Show it on the form.'])
    expect(summary.compareDocumentPosition(items[0]) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
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

  it('gives the author of a failed request the words "AI could not finish", with Send again and Drop…', async () => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u1', state: 'failed', plan })).fetch)
    renderPage()

    const guide = await screen.findByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('AI could not finish')).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Send again' })).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Drop…' })).toBeVisible()
  })

  it("sends a failed request's words and auto-accept choice as a new request, and opens its page", async () => {
    const server = serverOf(request({ author: 'u1', state: 'failed', plan, autoAccept: true }))
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Send again' }))

    await waitFor(() =>
      expect(screen.getByTestId('location').textContent).toBe(`/studio?app=${application}&view=requests&request=r2`),
    )
    expect(server.calls).toEqual([
      { action: 'authoring-requests', body: { text: 'Add a supplier field.', autoAccept: true } },
    ])
  })

  it('shows another reader the words of a failed request, with no Send again and no Drop…', async () => {
    vi.stubGlobal('fetch', serverOf(request({ author: 'u2', state: 'failed', plan })).fetch)
    renderPage()

    const guide = await screen.findByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('AI could not finish')).toBeVisible()
    expect(button('Send again')).toBeNull()
    expect(button('Drop…')).toBeNull()
  })

  it('shows "That did not work. Try again." when Send again is refused, and stays on the failed request', async () => {
    const server = serverOf(request({ author: 'u1', state: 'failed', plan }), ['authoring-requests'])
    vi.stubGlobal('fetch', server.fetch)
    vi.spyOn(console, 'warn').mockImplementation(() => undefined)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Send again' }))

    expect(await screen.findByTestId('studio-ai-request-error')).toHaveTextContent('That did not work. Try again.')
    expect(screen.getByTestId('studio-ai-request-state')).toHaveTextContent('Failed')
    expect(screen.getByTestId('location').textContent).toBe(`/studio?app=${application}&view=requests&request=r1`)
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

describe("the plan's groups", () => {
  const fullPlan: AuthoringPlan = {
    summary: 'Add a supplier field.',
    questions: [
      { question: 'Which type is the supplier?', options: ['Text', 'Reference', 'List'], recommended: 2 },
      { question: 'Is it required?', options: ['Yes', 'No'], recommended: 0 },
    ],
    changes: ['Add a Supplier field.'],
    decisions: [{ decision: 'Use a reference', reason: 'It keeps one supplier name.' }],
    risks: ['Old requests have no supplier.'],
    checks: ['A request saves with a supplier.'],
    checklist: [{ item: 'The draft does what the plan says.', how: 'It adds only the field.' }],
    details: ['Add the field to the Request entity.'],
  }
  const asking = (overrides: Partial<AuthoringRequest> = {}) =>
    request({ author: 'u1', state: 'awaitingApproval', plan: fullPlan, ...overrides })
  const button = (name: string) => screen.queryByRole('button', { name })

  it('shows the seven groups in the documented order', async () => {
    vi.stubGlobal('fetch', serverOf(asking()).fetch)
    renderPage()

    const groups = await screen.findAllByTestId('studio-ai-request-plan-group')
    expect(groups.map((group) => group.firstElementChild?.textContent)).toEqual([
      'Questions',
      'What changes',
      'Decisions to check',
      'Risks and limits',
      'Acceptance criteria',
      'Checklist',
      'Implementation notes',
    ])
  })

  it('shows no heading for an empty group', async () => {
    vi.stubGlobal('fetch', serverOf(asking({ plan: { ...fullPlan, risks: [] } })).fetch)
    renderPage()

    await screen.findAllByTestId('studio-ai-request-plan-group')
    expect(screen.queryByText('Risks and limits')).toBeNull()
    expect(screen.getByText('Decisions to check')).toBeVisible()
  })

  it('shows each decision with its reason and each checklist item with how the plan meets it', async () => {
    vi.stubGlobal('fetch', serverOf(asking()).fetch)
    renderPage()

    const items = (await screen.findAllByRole('listitem')).map((item) => item.textContent)
    expect(items).toContain('Use a referenceIt keeps one supplier name.')
    expect(items).toContain('The draft does what the plan says.It adds only the field.')
  })

  it('lists the options of each question and marks the recommended one', async () => {
    vi.stubGlobal('fetch', serverOf(asking()).fetch)
    renderPage()

    expect(await screen.findByText('Reference (recommended)')).toBeVisible()
    expect(screen.getByText('Text')).toBeVisible()
    expect(screen.getAllByText(/\(recommended\)/)).toHaveLength(1)
  })

  it('keeps the implementation notes hidden until their button is clicked', async () => {
    vi.stubGlobal('fetch', serverOf(asking()).fetch)
    renderPage()

    expect(screen.queryByText('Add the field to the Request entity.')).toBeNull()
    await userEvent.click(await screen.findByRole('button', { name: 'Show the implementation notes' }))
    expect(screen.getByText('Add the field to the Request entity.')).toBeVisible()
    await userEvent.click(screen.getByRole('button', { name: 'Hide the implementation notes' }))
    expect(screen.queryByText('Add the field to the Request entity.')).toBeNull()
  })

  it('tells its author that two questions wait and offers Answer… in place of Approve plan', async () => {
    vi.stubGlobal('fetch', serverOf(asking()).fetch)
    renderPage()

    const guide = await screen.findByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('2 questions to answer')).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Answer…' })).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Drop…' })).toBeVisible()
    expect(button('Approve plan')).toBeNull()
    expect(button('Change the plan…')).toBeNull()
  })

  it('says "1 question to answer" for one question', async () => {
    vi.stubGlobal('fetch', serverOf(asking({ plan: { ...fullPlan, questions: [fullPlan.questions[0]] } })).fetch)
    renderPage()

    expect(await screen.findByText('1 question to answer')).toBeVisible()
  })

  it('sends the answers as a change of plan', async () => {
    const server = serverOf(asking())
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Answer…' }))
    const dialog = await screen.findByRole('dialog', { name: "Answer the plan's questions" })
    expect(within(dialog).getByText('AI writes the plan again with your answers, and you approve it.')).toBeVisible()
    await userEvent.type(within(dialog).getByRole('textbox', { name: 'Your answers' }), 'A reference, required')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ask for a new plan' }))

    expect(await screen.findByText('AI is writing the plan')).toBeVisible()
    expect(server.calls).toEqual([{ action: 'change-plan', body: { comment: 'A reference, required' } }])
  })

  it('asks for the answers before it sends them', async () => {
    const server = serverOf(asking())
    vi.stubGlobal('fetch', server.fetch)
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Answer…' }))
    const dialog = await screen.findByRole('dialog', { name: "Answer the plan's questions" })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Ask for a new plan' }))

    expect(await within(dialog).findByText('Write your answers.')).toBeInTheDocument()
    expect(server.calls).toEqual([])
  })

  it('gives another reader no Answer… and the words of a plan that waits for its author', async () => {
    vi.stubGlobal('fetch', serverOf(asking({ author: 'u2' })).fetch)
    renderPage()

    expect(await screen.findByText("Waiting for its author's approval")).toBeVisible()
    expect(button('Answer…')).toBeNull()
    expect(button('Approve plan')).toBeNull()
    expect(button('Drop…')).toBeNull()
  })
})

describe('the feed', () => {
  const plan = planOf('Add a supplier field.')
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

    const conflict = await screen.findByTestId('studio-ai-request-conflict', undefined, { timeout: 4000 })
    const items = Array.from(screen.getByTestId('steps-bar').children) as HTMLElement[]
    expect(items[2]).toContainElement(conflict)
  })
})

describe('Preview the draft', () => {
  const ready = (overrides: Partial<AuthoringRequest> = {}) =>
    request({
      state: 'ready',
      author: 'u1',
      plan: planOf('Add a field.'),
      steps: [checkStep(passedChecks, 'passed')],
      draft: 'd1',
      ...overrides,
    })
  const previewHost = 'preview.example.test'

  it.each([
    ['another person', ready({ author: 'u2' }), previewHost],
    ['a deployment without a preview host', ready(), null],
    ['a request that is not ready', ready({ state: 'working' }), previewHost],
    ['a request without a draft', ready({ draft: null }), previewHost],
  ] as const)('is not shown for %s', async (_, shown, host) => {
    vi.stubGlobal('fetch', serverOf(shown).fetch)
    renderPage({ previewHost: host, resources: [site] })

    await screen.findByTestId('studio-ai-request-state')
    await screen.findByTestId('studio-ai-request-guide')
    expect(screen.queryByTestId('studio-ai-request-preview')).toBeNull()
  })

  it('sends the draft route, is off while the preview runs and ends the status line with a link named by each site', async () => {
    const server = serverOf(ready())
    server.preview = ['running', 'succeeded']
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ previewHost, resources: [site] })

    const button = await screen.findByTestId('studio-ai-request-preview')
    expect(button).toHaveTextContent('Preview the draft')
    await userEvent.click(button)

    expect(server.calls).toEqual([{ action: 'preview', body: undefined }])
    expect(server.fetch).toHaveBeenCalledWith(
      expect.stringContaining('/drafts/d1/preview'),
      expect.objectContaining({ method: 'POST' }),
    )
    expect(await screen.findByTestId('studio-request-status')).toHaveAttribute('data-status', 'queued')
    expect(screen.getByTestId('studio-ai-request-preview')).toBeDisabled()

    const link = await screen.findByTestId('studio-preview-link', undefined, { timeout: 4000 })
    expect(link).toHaveTextContent('Open Purchasing')
    expect(link.getAttribute('href')).toContain('//preview.example.test')
    expect(link.getAttribute('href')).toMatch(/\/purchasing$/)
    expect(screen.getByTestId('studio-request-status')).toHaveTextContent('The preview is ready. Open Purchasing')
    expect(screen.getByTestId('studio-ai-request-preview')).toBeEnabled()
    expect(screen.getByTestId('studio-ai-request-result')).toContainElement(link)
  })

  it('shows an error and no status line when the draft route is refused', async () => {
    vi.stubGlobal('fetch', serverOf(ready(), ['preview']).fetch)
    renderPage({ previewHost, resources: [site] })

    await userEvent.click(await screen.findByTestId('studio-ai-request-preview'))

    expect(await screen.findByTestId('studio-ai-request-preview-error')).toBeVisible()
    expect(screen.queryByTestId('studio-request-status')).toBeNull()
    expect(screen.getByTestId('studio-ai-request-preview')).toBeEnabled()
  })
})

describe('the Work item', () => {
  const approvedAt = '2026-11-18T09:05:00Z'
  const approved = { author: 'u1', plan: planOf('Add a supplier field.'), approvedAt }
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

describe('the History', () => {
  const approvedAt = '2026-11-18T09:05:00Z'
  const approved = { author: 'u1', plan: planOf('Add a supplier field.'), approvedAt }
  /** The time a number of minutes after 09:00 on the day of the request. */
  const at = (minutes: number) => new Date(Date.parse('2026-11-18T09:00:00Z') + minutes * 60_000).toISOString()
  /** A step that starts at a minute and runs a minute, unless `overrides` gives it another end. */
  const step = (kind: string, minute: number, overrides: Partial<AuthoringRequestStep> = {}): AuthoringRequestStep => ({
    kind,
    summary: null,
    outcome: null,
    tokens: 0,
    startedAt: at(minute),
    finishedAt: at(minute + 1),
    checks: null,
    resource: null,
    ...overrides,
  })
  /** Opens the History from Show the steps and returns its table. */
  const show = async () => {
    await userEvent.click(await screen.findByRole('button', { name: 'Show the steps' }))
    return screen.findByTestId('studio-ai-request-history')
  }
  /** The words of each body row, its Step and Result cells, newest first. */
  const words = (table: HTMLElement) =>
    within(table)
      .getAllByRole('row')
      .slice(1)
      .map((row) =>
        within(row)
          .getAllByRole('cell')
          .slice(0, 2)
          .map((cell) => cell.textContent),
      )

  it('folds the History open from Show the steps, closed at first', async () => {
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'ready', steps: [step('work', 6)] })).fetch)
    renderPage()

    const toggle = await screen.findByRole('button', { name: 'Show the steps' })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByTestId('studio-ai-request-history')).toBeNull()

    await userEvent.click(toggle)

    expect(screen.getByRole('button', { name: 'Hide the steps' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByTestId('studio-ai-request-history')).toBeVisible()
  })

  it('shows the table of its steps with four columns, newest step first', async () => {
    const steps = [
      step('plan', 0),
      step('planChange', 1, { summary: 'Fewer fields' }),
      step('work', 2, { outcome: null }),
      step('check', 3, { outcome: 'passed', checks: passedChecks as AuthoringRequestStep['checks'] }),
      step('review', 4, { outcome: 'passed' }),
      step('check', 5, { outcome: 'failed', checks: failedCompile as AuthoringRequestStep['checks'] }),
    ]
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'ready', steps })).fetch)
    renderPage()

    const table = await show()

    expect(
      within(table)
        .getAllByRole('columnheader')
        .map((header) => header.textContent),
    ).toEqual(['Step', 'Result', 'Started', 'Duration'])
    expect(words(table)).toEqual([
      ['Checked the draft', 'It does not compile: 2 problems'],
      ['Reviewed the draft', 'No changes requested'],
      ['Checked the draft', 'All scenarios pass'],
      ['Changed the draft', 'Saved'],
      ['Asked for another plan: “Fewer fields”', '—'],
      ['Wrote a plan', 'Plan written'],
    ])
  })

  it('shows an em dash in Duration for a step that is still running, and no pager with more than ten steps', async () => {
    const steps = Array.from({ length: 11 }, (_, index) => step('work', index * 2))
    steps.push(step('check', 22, { finishedAt: null }))
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'checking', steps })).fetch)
    renderPage()

    const table = await show()

    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(12)
    expect(within(rows[0]).getAllByRole('cell')[3]).toHaveTextContent('—')
    expect(within(rows[1]).getAllByRole('cell')[3]).not.toHaveTextContent('—')
    expect(within(table).queryByRole('list')).toBeNull()
  })

  /** A saved write at a minute, of the tool given, with its resource. */
  const write = (
    minute: number,
    resource: { kind: string; id: string; name: string },
    summary = 'write_resource',
    overrides: Partial<AuthoringRequestStep> = {},
  ) => step('write', minute, { summary, outcome: 'saved', resource, ...overrides })
  const form = { kind: 'form', id: 'form-1', name: 'RequestForm' }
  const entity = { kind: 'entity', id: 'entity-1', name: 'Supplier' }
  const role = { kind: 'role', id: 'role-1', name: 'approver' }

  it('folds a run of writes into one row, "Wrote 3 resources", that opens to each write', async () => {
    const steps = [
      step('work', 2),
      write(3, form),
      write(4, entity, 'write_layer'),
      write(5, role),
      step('check', 6, { outcome: 'passed', checks: passedChecks as AuthoringRequestStep['checks'] }),
    ]
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'ready', steps })).fetch)
    renderPage({
      resources: [
        { id: 'form-1', kind: 'form', name: 'RequestForm', file: 'forms/request.json', version: 'v1', usedBy: 0 },
      ],
    })

    const table = await show()

    expect(words(table)).toEqual([
      ['Checked the draft', 'All scenarios pass'],
      ['Wrote 3 resources', 'Saved'],
      ['Changed the draft', 'Saved'],
    ])
    await userEvent.click(within(table).getByRole('button', { name: 'Show the rows in it' }))
    expect(words(table).slice(2, 5)).toEqual([
      ['Wrote approver Role', 'Saved'],
      ['Wrote Supplier Entity', 'Saved'],
      ['Wrote RequestForm Form', 'Saved'],
    ])
    // The resource the application holds is a link, and one that only the draft holds is plain text.
    expect(within(table).getByRole('link', { name: 'RequestForm' })).toHaveAttribute(
      'href',
      expect.stringContaining('form-1'),
    )
    expect(within(table).getByText('Supplier').closest('a')).toBeNull()
    expect(within(table).queryByRole('list')).toBeNull()
  })

  it('reads "Changed 2 resources" when a delete is among the writes, "Results differ" when the results differ, and keeps one write a plain row', async () => {
    const steps = [
      write(1, form),
      write(2, entity, 'delete_resource', { outcome: 'refused' }),
      step('check', 3),
      write(4, role),
    ]
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'ready', steps })).fetch)
    renderPage()

    const table = await show()

    expect(words(table)).toEqual([
      ['Wrote approver Role', 'Saved'],
      ['Checked the draft', '—'],
      ['Changed 2 resources', 'Results differ'],
    ])
    await userEvent.click(within(table).getByRole('button', { name: 'Show the rows in it' }))
    expect(words(table).slice(3, 5)).toEqual([
      ['Deleted Supplier Entity', 'Refused'],
      ['Wrote RequestForm Form', 'Saved'],
    ])
  })

  it('shows Show the steps to a reader who is not the author', async () => {
    vi.stubGlobal('fetch', serverOf(request({ ...approved, state: 'ready', steps: [step('work', 6)] })).fetch)
    renderPage({ userId: 'u9' })

    expect(await screen.findByRole('button', { name: 'Show the steps' })).toBeVisible()
  })
})

describe('the Plan item of an approved plan', () => {
  const approvedAt = '2026-11-18T09:05:00Z'
  const plan = planOf('Add a supplier field.', ['Show it on the form.'])
  const asked = (comment: string): AuthoringRequestStep => ({
    kind: 'planChange',
    summary: comment,
    outcome: null,
    tokens: 0,
    startedAt: sentAt,
    finishedAt: sentAt,
    checks: null,
    resource: null,
  })
  const planItem = async () => (Array.from((await screen.findByTestId('steps-bar')).children) as HTMLElement[])[0]

  it('reads "Approved on <time>", folds the plan under its summary and shows its changes with Show the plan', async () => {
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

  it('shows no Show the plan for a plan with no changes', async () => {
    vi.stubGlobal(
      'fetch',
      serverOf(request({ author: 'u1', state: 'working', plan: planOf('Add a field.'), approvedAt })).fetch,
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

  it('lists each resource of the diff open, and names a layer by its module resource with its marks', async () => {
    const server = serverOf(request({ state: 'ready', author: 'u1', steps: [checkStep(passedChecks, 'passed')] }))
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ userId: 'u1' })

    const files = await screen.findAllByTestId('studio-changes-file')
    expect(files).toHaveLength(2)

    const layer = files.find((file) => file.dataset.file === 'customizations/purchase-request.json')!
    expect(within(layer).getByText('PurchaseRequest')).toBeVisible()
    expect(within(layer).getByRole('note', { name: 'supplier, added by this application' })).toHaveTextContent('Added')
    expect(within(layer).getByRole('note', { name: 'amount, changed by this application' })).toHaveTextContent(
      'Changed',
    )
    expect(within(layer).getByRole('note', { name: 'notes, removed by this application' })).toHaveTextContent('Removed')
    expect(within(layer).queryByRole('button', { name: /Reset/ })).not.toBeInTheDocument()

    expect(within(layer).getByRole('button', { name: 'Hide changes to PurchaseRequest', expanded: true })).toBeVisible()
    expect(await within(layer).findByRole('textbox', { name: 'PurchaseRequest' })).toBeVisible()
    await waitFor(() =>
      expect(Array.from(layer.querySelectorAll('.cm-changedLine')).map((line) => line.textContent)).toContainEqual(
        expect.stringContaining('"supplier"'),
      ),
    )
  })

  it('shows a changed package file open, drawing its line diff', async () => {
    const packageFile = {
      file: 'packages/approval-rules/adapter.py',
      change: 'changed' as const,
      id: null,
      kind: null,
      name: null,
      before: 'def limit():\n    return 1000\n',
      after: 'def limit():\n    return 5000\n',
      target: null,
    }
    const server = serverOf(request({ state: 'ready', author: 'u1', steps: [checkStep(passedChecks, 'passed')] }), [], {
      ...diff,
      resources: [...diff.resources, packageFile],
    })
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ userId: 'u1' })

    const files = await screen.findAllByTestId('studio-changes-file')
    const section = files.find((file) => file.dataset.file === packageFile.file)!
    expect(within(section).getByText('Package file')).toBeVisible()

    expect(await within(section).findByRole('textbox', { name: 'packages/approval-rules/adapter.py' })).toBeVisible()
    await waitFor(() => expect(section.querySelector('.cm-deletedLine')).toHaveTextContent('return 1000'))
    expect(section.querySelector('.cm-changedLine')).toHaveTextContent('return 5000')
  })

  it("links each scenario's name to its Studio address in the checks, and leaves a skipped scenario's name as text", async () => {
    const skipped = {
      name: 'department-budgets/X',
      file: 'scenarios/x.json',
      outcome: 'skipped',
      point: 'Budget/effects/decisionRecorded',
    }
    const checks = { ...passedChecks, scenarios: { ...passedChecks.scenarios, items: [scenario, skipped] } }
    const server = serverOf(request({ state: 'ready', author: 'u1', steps: [checkStep(checks, 'passed')] }))
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ userId: 'u1' })

    const summary = await screen.findByTestId('studio-ai-request-checks')
    await userEvent.click(within(summary).getByRole('button', { name: 'Show the checks' }))
    const [passed, muted] = within(screen.getByTestId('studio-ai-request-checks-detail')).getAllByTestId(
      'studio-test-result',
    )
    expect(within(passed).getByRole('link', { name: 'ApprovalBelowThreshold' })).toHaveAttribute(
      'href',
      studioResourceAddress(application, 'r1'),
    )
    expect(within(muted).getByText('department-budgets/X')).toBeVisible()
    expect(within(muted).queryByRole('link')).not.toBeInTheDocument()
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
      plan: planOf('Add a field.'),
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

  it('shows a conflict alert with a bullet for each changed resource, and Make the change again and Drop… with no Accept', async () => {
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
    expect(items[0].textContent).toBe(`Employee Role, changed by Linh Nguyen on ${timeOf('2026-11-18T10:15:00Z')}`)
    expect(within(items[0]).getByRole('link', { name: 'Employee' })).toHaveAttribute(
      'href',
      studioResourceAddress(application, 'role-1'),
    )
    expect(within(items[0]).getByText(timeOf('2026-11-18T10:15:00Z'), { selector: 'time' })).toHaveAttribute(
      'datetime',
      '2026-11-18T10:15:00Z',
    )
    expect(items[1].textContent).toBe(
      `packages/tax/rates.json, changed by Minh Tran on ${timeOf('2026-11-18T11:40:00Z')}`,
    )
    expect(within(items[1]).queryByRole('link')).toBeNull()

    const guide = screen.getByTestId('studio-ai-request-guide')
    expect(within(guide).getByText('Resources changed since AI read them')).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Make the change again' })).toBeVisible()
    expect(within(guide).getByRole('button', { name: 'Drop…' })).toBeVisible()
    expect(within(guide).queryByRole('button', { name: 'Accept' })).toBeNull()
    expect(screen.queryByText('Not accepted. Nothing in the application changed.')).toBeNull()
    expect(screen.queryByTestId('studio-request-status')).toBeNull()
  })

  it('posts the retry when the author clicks "Make the change again", and then reads "AI is working on it"', async () => {
    const server = serverOf(ready())
    server.accept = ['queued', 'failed']
    server.acceptDiagnostics = [conflict('role-1', 'roles/employee.json', 'Linh Nguyen', '2026-11-18T10:15:00Z')]
    vi.stubGlobal('fetch', server.fetch)
    renderPage({ resources: [employee] })
    await userEvent.click(await accept())
    await screen.findByTestId('studio-ai-request-conflict', undefined, { timeout: 4000 })

    await userEvent.click(screen.getByRole('button', { name: 'Make the change again' }))

    expect(server.calls.at(-1)).toEqual({ action: 'retry', body: undefined })
    const guide = screen.getByTestId('studio-ai-request-guide')
    expect(await within(guide).findByText('AI is working on it')).toBeVisible()
    expect(screen.queryByTestId('studio-ai-request-conflict')).toBeNull()
    expect(within(guide).queryByRole('button', { name: 'Make the change again' })).toBeNull()
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
