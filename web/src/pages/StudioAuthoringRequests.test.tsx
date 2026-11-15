import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioAuthoringRequestDetail, StudioAuthoringRequestStep } from '../platform/studio'
import { designTokensFor } from '../platform/designTokens'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { requestTitle } from './authoringRequests'
import { StudioAuthoringRequestPage, StudioAuthoringRequests } from './StudioAuthoringRequests'

const application = 'Purchases'
const userId = 'b6f1c2d3-0000-4000-8000-000000000001'

// Proposed: the work of an outside AI app through the MCP server, see the review on #3263.
const fromApp: StudioAuthoringRequestDetail = {
  id: 'a1e00000-0000-4000-8000-000000000007',
  application,
  author: userId,
  authorName: 'Alex Morgan',
  text: 'Add a Cost centre field to the purchase request.',
  title: 'Add a Cost centre field',
  app: 'Claude Code',
  coAuthors: ['Claude Code'],
  state: 'ready',
  plan: {
    summary: 'Adds a Cost centre field to each purchase request.',
    questions: [],
    changes: ['A Cost centre field on each purchase request.'],
    decisions: [],
    risks: [],
    checks: [],
    checklist: [],
    details: [],
  },
  aiConnected: false,
  round: 1,
  tokens: 0,
  autoAccept: false,
  sentAt: '2026-11-10T09:15:00Z',
  approvedAt: null,
  finishedAt: null,
  steps: [
    {
      kind: 'validate_draft',
      summary: null,
      outcome: 'passed',
      checks: { compile: { outcome: 'passed', diagnostics: [] } },
      tokens: 0,
      startedAt: '2026-11-10T09:20:00Z',
      finishedAt: '2026-11-10T09:21:00Z',
    },
  ],
  draft: null,
  checks: {
    id: 'acc00000-0000-4000-8000-000000000007',
    application,
    kind: 'test',
    target: 'tenant',
    status: 'succeeded',
    label: null,
    note: null,
    releaseId: null,
    diagnostics: [],
    scenarios: [],
  },
  review: null,
  changes: [],
  failure: null,
  accept: null,
}

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function stub(answer: unknown) {
  const fetchMock = vi.fn(async (_input: string, _init?: RequestInit) => json(answer))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderIn(children: React.ReactNode) {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          {children}
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

describe('an outside AI app’s request', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('lists it as any request, with no word of the app', async () => {
    stub({ items: [fromApp] })
    renderIn(<StudioAuthoringRequests application={application} userId={userId} />)

    expect(await screen.findByText('You')).toBeInTheDocument()
    expect(screen.queryByText(/Claude Code/)).toBeNull()
  })

  it('names its co-author, shows its plan and Accept, and says that no second AI reviewed it', async () => {
    stub(fromApp)
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    expect(await screen.findByText(/^Sent by you on .*, co-authored with Claude Code$/)).toBeInTheDocument()
    // The request's words, signed by who sent it, come right under the heading.
    const asked = screen.getByTestId('studio-ai-asked')
    expect(within(asked).getByText('Ready')).toHaveAttribute('data-tone', 'attention')
    expect(within(asked).getByText('Add a Cost centre field to the purchase request.')).toBeInTheDocument()
    expect(within(asked).getByText(/^Sent by you on .*, co-authored with Claude Code$/)).toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole('button', { name: 'Show the plan' }))
    expect(await screen.findByTestId('studio-ai-plan')).toHaveTextContent(
      'A Cost centre field on each purchase request.',
    )
    expect(screen.getByRole('button', { name: 'Accept' })).toBeInTheDocument()
    const notReviewed = screen.getByTestId('studio-ai-not-reviewed')
    expect(within(notReviewed).getByText('Not reviewed')).toHaveAttribute('data-tone', 'muted')
    expect(notReviewed).toHaveTextContent('Connect your AI to have a second AI review changes.')
  })
})

describe('an outside AI app’s request while it works', () => {
  it('runs here through the same stages as any request once its author has connected their AI', async () => {
    stub({ ...fromApp, state: 'working', checks: null, aiConnected: true })
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    expect(await screen.findByText('Working')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-ai-app-works')).toBeNull()
    // Every request reads as the same feed, whichever way it began: its plan done, its work going on.
    expect(screen.getByTestId('studio-ai-feed-plan')).toHaveAttribute('data-mark', 'done')
    expect(screen.getByTestId('studio-ai-feed-work')).toHaveAttribute('data-mark', 'now')
    vi.unstubAllGlobals()
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('waits for the app, with no spinner, while its author has no AI here', async () => {
    stub({ ...fromApp, state: 'working', checks: null })
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    expect(await screen.findByTestId('studio-ai-guide')).toHaveTextContent(
      'Connect your AI to have AI go on here, or go on in Claude Code, and it reads what you asked here.',
    )
    expect(screen.getByTestId('studio-ai-feed-work').querySelector('.ant-spin')).toBeNull()
  })
})

// Proposed: the author asks AI to change a ready draft, see the review on #3263.
describe('a ready request', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('asks AI for changes with the author’s comment', async () => {
    const fetchMock = stub(fromApp)
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )
    const user = userEvent.setup()

    // Proposed: the author replies under the feed, as a comment on a pull request asks its agent for more.
    await user.click(await screen.findByRole('button', { name: 'Ask for other changes…' }))
    const reply = await screen.findByTestId('studio-ai-reply')
    await user.type(
      within(reply).getByRole('textbox', { name: 'What should AI change?' }),
      'Make the cost centre optional.',
    )
    await user.click(within(reply).getByRole('button', { name: 'Ask AI to change it' }))

    await waitFor(() => {
      const post = fetchMock.mock.calls.find(
        ([url, init]) => init?.method === 'POST' && url.endsWith('/request-changes'),
      )
      expect(post?.[1]?.body).toBe(JSON.stringify({ comment: 'Make the cost centre optional.' }))
    })
  })
})

// Proposed: a review's must-change findings and notes, see the review on #3263.
describe('a ready request’s review', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function renderPage() {
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )
  }

  it('gives its verdict as a tag, then its findings grouped by the resource they are about', async () => {
    stub({
      ...fromApp,
      aiConnected: true,
      review: {
        summary: 'Mails the approver too.',
        findings: [
          { blocking: false, text: 'Could say why.', file: 'pages/list.json', path: '/widgets/1', resourceId: 'r-1' },
          {
            blocking: true,
            text: 'Mails the approver too.',
            file: 'pages/list.json',
            path: '/widgets/0',
            resourceId: 'r-1',
          },
          { blocking: false, text: 'A loose end.', file: 'misc.json', path: '', resourceId: null },
        ],
      },
    })
    const onOpen = vi.fn()
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[
          {
            id: 'r-1',
            kind: 'page',
            name: 'RequestList',
            file: 'pages/list.json',
            version: 'v',
            usedBy: 0,
            titles: { en: 'Requests' },
          },
        ]}
        onOpen={onOpen}
      />,
    )

    const review = await screen.findByTestId('studio-ai-review')
    expect(within(review).getByText('Changes requested')).toHaveAttribute('data-tone', 'attention')
    expect(within(review).queryByText(/Approved/)).toBeNull()
    // Proposed: the blocking findings, their heading in dangerText, then the non-blocking ones, each group under its heading and by the resource it is about.
    const toFix = within(review).getByTestId('studio-ai-findings-blocking')
    expect(within(toFix).getByText('Blocking findings')).toHaveStyle({
      color: designTokensFor('light').colour.dangerText,
    })
    const [fixPage] = within(toFix).getAllByTestId('studio-problem-group')
    expect(within(fixPage).getAllByRole('button', { name: 'Requests' })).toHaveLength(1)
    expect(within(fixPage).getByText('Page')).toBeInTheDocument()
    expect(fixPage).toHaveTextContent('Mails the approver too.')
    // Proposed: a finding is a sentence with no lead, so it takes a bullet, its words at spacing-xl.
    expect(within(fixPage).getByTestId('studio-problem').parentElement).toHaveStyle({
      listStyleType: 'disc',
      paddingInlineStart: '32px',
    })
    const notes = within(review).getByTestId('studio-ai-findings-non-blocking')
    expect(within(notes).getByText('Non-blocking findings')).toBeInTheDocument()
    const [notePage, loose] = within(notes).getAllByTestId('studio-problem-group')
    expect(notePage).toHaveTextContent('Could say why.')
    expect(within(loose).getByText('misc.json')).toBeInTheDocument()
    expect(within(loose).queryByRole('button')).toBeNull()

    const user = userEvent.setup()
    await user.click(within(fixPage).getByRole('button', { name: 'Requests' }))
    expect(onOpen).toHaveBeenLastCalledWith('r-1', '/widgets/0')
    await user.click(within(notePage).getByRole('button', { name: 'Requests' }))
    expect(onOpen).toHaveBeenLastCalledWith('r-1', '/widgets/1')
  })

  it('says no changes are requested when nothing must change', async () => {
    stub({ ...fromApp, aiConnected: true, review: { summary: 'All done.', findings: [] } })
    renderPage()

    const review = await screen.findByTestId('studio-ai-review')
    expect(within(review).getByText('No changes requested')).toHaveAttribute('data-tone', 'success')
    expect(within(review).queryByTestId('studio-problem-group')).toBeNull()
  })
})

describe('a ready request’s page', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reads as one feed, its plan and its work done, then its result, with the changes and the checks unfolded on asking', async () => {
    const fetchMock = vi.fn(async (input: string) =>
      input.endsWith('/diff')
        ? json({
            cursor: null,
            resources: [
              {
                file: 'entities/cost-centre.json',
                change: 'added',
                id: null,
                kind: 'entity',
                name: 'CostCentre',
                before: null,
                after: '{}',
                titles: { en: 'Cost centre' },
              },
            ],
          })
        : json({
            ...fromApp,
            aiConnected: true,
            draft: 'd-1',
            review: { summary: 'All done.', findings: [] },
            changes: [{ file: 'entities/cost-centre.json', summary: 'A cost centre with a name and a code.' }],
            checks: {
              ...fromApp.checks!,
              scenarios: [
                {
                  name: 's',
                  resourceId: 'r',
                  file: 'f',
                  outcome: 'passed',
                  durationMs: 1,
                  step: null,
                  path: null,
                  message: null,
                },
              ],
            },
          }),
    )
    vi.stubGlobal('fetch', fetchMock)
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    const checks = await screen.findByTestId('studio-ai-checks')
    expect(within(checks).getByText('Passed')).toHaveAttribute('data-tone', 'success')
    expect(within(checks).getByText('The scenario passes')).toBeInTheDocument()
    expect(screen.getByTestId('studio-ai-guide')).toHaveTextContent('Checks and review passed')
    expect(screen.queryByRole('tab')).toBeNull()
    expect(screen.getByTestId('studio-ai-feed-plan')).toHaveAttribute('data-mark', 'done')
    const result = screen.getByTestId('studio-ai-feed-result')
    expect(result).toHaveAttribute('data-mark', 'waiting')
    // Proposed: what the draft changes is its diff, each resource as people read it with the summary its request wrote,
    // folded until its author opens it.
    const file = await within(result).findByTestId('studio-revision-change')
    expect(file).toHaveTextContent(/Cost centre\s*Entity\s*Added.*A cost centre with a name and a code\./)
    const user = userEvent.setup()
    const toggle = within(file).getByRole('button', { name: /Cost centre/ })
    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    await user.click(toggle)
    expect(toggle).toHaveAttribute('aria-expanded', 'true')
    expect(fetchMock).toHaveBeenCalledWith(
      `/api/studio/apps/${application}/authoring-requests/${fromApp.id}/diff`,
      expect.anything(),
    )
    expect(screen.queryByTestId('studio-ai-check-details')).toBeNull()
    await user.click(within(result).getByRole('button', { name: 'Show the checks' }))
    expect(screen.getByTestId('studio-ai-check-details')).toBeInTheDocument()
  })

  it('previews the draft on the preview host, then links each site there', async () => {
    const preview = { ...fromApp.checks!, id: 'p-1', kind: 'publish', target: 'preview', status: 'succeeded' }
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) =>
      input.endsWith('/drafts/d-1/preview') && init?.method === 'POST'
        ? json(preview)
        : input.includes('/resources/s-1')
          ? json({ id: 's-1', kind: 'site', name: 'Requests', file: 'sites/requests.json', content: { path: 'buy' } })
          : input.includes('/diff')
            ? json({ cursor: null, resources: [] })
            : json({ ...fromApp, draft: 'd-1' }),
    )
    vi.stubGlobal('fetch', fetchMock)
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[
          { id: 's-1', kind: 'site', name: 'Requests', file: 'sites/requests.json', version: 'v', usedBy: 0 },
        ]}
        onOpen={() => {}}
        previewHost="preview.example.com"
      />,
    )

    await userEvent.setup().click(await screen.findByRole('button', { name: 'Preview the draft' }))
    expect(await screen.findByTestId('studio-ai-preview-link')).toHaveAttribute(
      'href',
      expect.stringMatching(/\/\/preview\.example\.com(:\d+)?\/buy$/),
    )
  })

  it('shows no preview without a preview host', async () => {
    stub({ ...fromApp, draft: 'd-1' })
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    await screen.findByTestId('studio-ai-feed-result')
    expect(screen.queryByRole('button', { name: 'Preview the draft' })).toBeNull()
  })

  it('shows its plan while it waits for its author, with Approve plan in the bar and a reply that asks for another', async () => {
    const fetchMock = stub({ ...fromApp, state: 'awaitingApproval', checks: null, review: null, aiConnected: true })
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    const plan = await screen.findByTestId('studio-ai-feed-plan')
    expect(plan).toHaveAttribute('data-mark', 'waiting')
    expect(plan).toHaveTextContent('Waits for your approval')
    expect(within(plan).getByTestId('studio-ai-plan')).toBeInTheDocument()
    expect(screen.queryByTestId('studio-ai-feed-work')).toBeNull()
    const bar = screen.getByTestId('studio-ai-bar')
    expect(bar).toHaveTextContent('Plan ready for your approval')
    expect(within(bar).getByRole('button', { name: 'Approve plan' })).toBeInTheDocument()

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Change the plan…' }))
    const reply = screen.getByTestId('studio-ai-reply')
    await user.type(within(reply).getByRole('textbox', { name: 'What should the plan do differently?' }), 'No column.')
    await user.click(within(reply).getByRole('button', { name: 'Ask for a new plan' }))
    await waitFor(() => {
      const post = fetchMock.mock.calls.find(([url, init]) => init?.method === 'POST' && url.endsWith('/change-plan'))
      expect(post?.[1]?.body).toBe(JSON.stringify({ comment: 'No column.' }))
    })
  })
})

// Proposed: the feed marks where a request is in every state, see the review on #3263.
describe("a request's feed", () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const feedOf = async (request: Partial<StudioAuthoringRequestDetail>) => {
    stub({ ...fromApp, ...request })
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )
    return await screen.findByTestId('studio-ai-feed')
  }
  const workStep = (outcome: string | null): StudioAuthoringRequestStep => ({
    kind: 'fix',
    summary: null,
    outcome,
    tokens: 0,
    startedAt: '2026-11-10T09:00:00Z',
    finishedAt: '2026-11-10T09:30:00Z',
  })

  it('marks where a failed request stopped, in the work AI did after its plan was approved, and says why there', async () => {
    const feed = await feedOf({
      state: 'failed',
      checks: null,
      review: null,
      approvedAt: '2026-11-10T08:59:00Z',
      failure: 'Anthropic refused the request.',
      steps: [workStep('unfinished')],
    })
    expect(within(feed).getByTestId('studio-ai-feed-plan')).toHaveAttribute('data-mark', 'done')
    const work = within(feed).getByTestId('studio-ai-feed-work')
    expect(work).toHaveAttribute('data-mark', 'stopped')
    expect(within(work).getByTestId('studio-ai-failure')).toHaveTextContent('Anthropic refused the request.')
  })

  it('ends a dropped request with when it was dropped', async () => {
    const feed = await feedOf({
      state: 'dropped',
      checks: null,
      review: null,
      approvedAt: null,
      finishedAt: '2026-11-10T09:00:00Z',
      steps: [],
    })
    expect(within(feed).getByTestId('studio-ai-feed-plan')).toHaveAttribute('data-mark', 'idle')
    expect(within(feed).getByTestId('studio-ai-feed-dropped')).toHaveTextContent('Dropped')
  })
})

// Proposed: every request names the AIs that made its change, see the review on #3263.
describe('a request’s co-authors', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it.each([
    [['Claude Sonnet 5.5'], 'co-authored with Claude Sonnet 5.5'],
    [['Claude Code', 'Claude Sonnet 5.5'], 'co-authored with Claude Code and Claude Sonnet 5.5'],
  ])('names %j in the sender line', async (coAuthors, words) => {
    stub({ ...fromApp, coAuthors })
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    expect(await screen.findByText(new RegExp(`^Sent by you on .*, ${words}$`))).toBeInTheDocument()
  })
})

// Proposed: a fix round reads as working, at the Change stage, see the review on #3263.
describe('a request in a fix round', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('reads as Working', async () => {
    stub({ ...fromApp, state: 'fixing', round: 2, checks: null, aiConnected: true })
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    expect(await screen.findByText('Working')).toHaveAttribute('data-tone', 'neutral')
    expect(screen.queryByText('Fixing')).toBeNull()
    expect(within(screen.getByTestId('studio-ai-feed-work')).getByText('Fix round 2')).toBeInTheDocument()
  })
})

// Proposed: a request's short name heads its page and names it in the table, see the review on #3263.
describe('a request’s name', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('heads its page, with its state leading the line of who sent it, and its words under it', async () => {
    stub(fromApp)
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

    expect(await screen.findByRole('heading', { name: 'Add a Cost centre field' })).toBeInTheDocument()
    const asked = screen.getByTestId('studio-ai-asked')
    expect(within(asked).getByText('Ready')).toHaveAttribute('data-tone', 'attention')
    expect(within(asked).getByText('Add a Cost centre field to the purchase request.')).toBeInTheDocument()
  })

  it('names it in the table of AI requests', async () => {
    stub({ items: [fromApp] })
    renderIn(<StudioAuthoringRequests application={application} userId={userId} />)

    expect(await screen.findByRole('link', { name: 'Add a Cost centre field' })).toBeInTheDocument()
  })

  it('is the first sentence of its words, cut at 80 characters, until AI gives it one', () => {
    expect(requestTitle({ ...fromApp, title: null, text: 'Rename a field. Then show it.' })).toBe('Rename a field')
    expect(requestTitle({ ...fromApp, title: null, text: 'x'.repeat(100) })).toBe(`${'x'.repeat(79)}…`)
    expect(requestTitle(fromApp)).toBe('Add a Cost centre field')
  })
})

// Proposed: a plan's summary and the choices it made, and the review's summary and plan steps, see the review on #3263.
describe('a plan and its review, as a pull request has them', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function renderPage(onOpen = vi.fn()) {
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[
          {
            id: 'r-1',
            kind: 'page',
            name: 'List',
            file: 'pages/list.json',
            version: 'v',
            usedBy: 0,
            titles: { en: 'Requests' },
          },
        ]}
        onOpen={onOpen}
      />,
    )
    return onOpen
  }

  it("shows the whole plan, as a pull request's plan, with its details folded until asked for", async () => {
    stub({
      ...fromApp,
      state: 'awaitingApproval',
      checks: null,
      review: null,
      aiConnected: true,
      plan: {
        summary: 'Adds a Cost centre field.',
        questions: [],
        changes: ['A Cost centre field on each request.'],
        decisions: [{ decision: 'The field is optional.', reason: 'The request did not say.' }],
        risks: ['Saved requests have no cost centre.'],
        checks: ['A request saves with a cost centre.'],
        checklist: [{ item: 'Texts.', answer: 'In English and Vietnamese.' }],
        details: [
          { resourceId: 'r-1', file: 'pages/list.json', name: null, kind: null, edits: ['A Cost centre column.'] },
          {
            resourceId: null,
            file: 'entities/cost-centre.json',
            name: 'Cost centre',
            kind: 'entity',
            edits: ['A new entity.'],
          },
        ],
      },
    })
    renderPage()

    await screen.findByTestId('studio-ai-plan')
    expect(within(screen.getByTestId('studio-ai-plan-changes')).getByText('What changes')).toBeInTheDocument()
    // Proposed: the plan's points are sentences, a bullet each, their words at spacing-xl.
    expect(within(screen.getByTestId('studio-ai-plan-changes')).getByRole('list')).toHaveStyle({
      listStyleType: 'disc',
      paddingInlineStart: '32px',
    })
    const decisions = screen.getByTestId('studio-ai-plan-decisions')
    expect(within(decisions).getByText('Decisions to check')).toBeInTheDocument()
    // Each choice reads as text, and why, muted, after it.
    expect(within(decisions).getByText('The request did not say.')).toHaveStyle({
      color: designTokensFor('light').colour.textMuted,
    })
    expect(decisions).toHaveTextContent('The field is optional. The request did not say.')
    const risks = screen.getByTestId('studio-ai-plan-risks')
    expect(within(risks).getByText('Risks and limits')).toBeInTheDocument()
    expect(risks).toHaveTextContent('Saved requests have no cost centre.')
    const criteria = screen.getByTestId('studio-ai-plan-checks')
    expect(within(criteria).getByText('Acceptance criteria')).toBeInTheDocument()
    expect(criteria).toHaveTextContent('A request saves with a cost centre.')
    // Proposed: Axis's checklist, each item with how the plan answers it, muted.
    const checklist = screen.getByTestId('studio-ai-plan-checklist')
    expect(within(checklist).getByText('Checklist')).toBeInTheDocument()
    expect(within(checklist).getByText('In English and Vietnamese.')).toHaveStyle({
      color: designTokensFor('light').colour.textMuted,
    })
    expect(screen.queryByText('A Cost centre column.')).toBeNull()

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: 'Show the implementation notes' }))
    // The details list as the review's findings do, each resource once, and one only the draft adds by its name, as text.
    const details = screen.getByTestId('studio-ai-plan-details')
    const [page, added] = within(details).getAllByTestId('studio-problem-group')
    expect(within(page).getByRole('button', { name: 'Requests' })).toBeInTheDocument()
    expect(page).toHaveTextContent('A Cost centre column.')
    expect(added).toHaveTextContent(/^Cost centre\s*Entity\s*A new entity\.$/)
    expect(within(added).queryByRole('button', { name: 'Cost centre' })).toBeNull()
    expect(within(added).queryByText('entities/cost-centre.json')).toBeNull()
    expect(screen.getByRole('button', { name: 'Approve plan' })).toBeInTheDocument()
  })

  it("shows the plan's summary in the feed while the plan is folded", async () => {
    stub({ ...fromApp, aiConnected: true })
    renderPage()

    const plan = await screen.findByTestId('studio-ai-feed-plan')
    // The fold follows the summary on its line, as See the checks follows the checks' summary.
    expect(within(plan).getByTestId('studio-ai-plan-summary')).toHaveTextContent(
      'Adds a Cost centre field to each purchase request. Show the plan',
    )
    expect(within(plan).queryByTestId('studio-ai-plan')).toBeNull()
  })

  it('waits for the answers to its questions before it can be approved', async () => {
    const fetchMock = stub({
      ...fromApp,
      state: 'awaitingApproval',
      checks: null,
      review: null,
      aiConnected: true,
      plan: { ...fromApp.plan!, questions: ['Is the field required?'] },
    })
    renderPage()

    const questions = await screen.findByTestId('studio-ai-plan-questions')
    expect(questions).toHaveTextContent(/Questions.*Is the field required\?/)
    const bar = screen.getByTestId('studio-ai-bar')
    expect(bar).toHaveTextContent('1 question to answer')
    expect(within(bar).queryByRole('button', { name: 'Approve plan' })).toBeNull()

    const user = userEvent.setup()
    await user.click(within(bar).getByRole('button', { name: 'Answer…' }))
    const reply = screen.getByTestId('studio-ai-reply')
    await user.type(within(reply).getByRole('textbox', { name: 'Your answers' }), 'Yes, required.')
    await user.click(within(reply).getByRole('button', { name: 'Ask for a new plan' }))
    await waitFor(() => {
      const post = fetchMock.mock.calls.find(([url, init]) => init?.method === 'POST' && url.endsWith('/change-plan'))
      expect(post?.[1]?.body).toBe(JSON.stringify({ comment: 'Yes, required.' }))
    })
  })

  it('sums the review up in the result', async () => {
    stub({
      ...fromApp,
      aiConnected: true,
      review: { summary: 'The field is added, but the list shows no column.', findings: [] },
    })
    renderPage()

    const review = await screen.findByTestId('studio-ai-review')
    expect(within(review).getByText('The field is added, but the list shows no column.')).toBeInTheDocument()
  })
})

// Proposed: the history puts each step in Studio's own words from its codes, reads writes in a row as one, and says how
// long each step took, see the review on #3263.
describe("a request's history", () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const step = (
    kind: StudioAuthoringRequestStep['kind'],
    start: string,
    end: string | null,
    outcome: string | null,
    more: Partial<StudioAuthoringRequestStep> = {},
  ): StudioAuthoringRequestStep => ({
    kind,
    summary: null,
    outcome,
    tokens: 0,
    startedAt: start,
    finishedAt: end,
    ...more,
  })
  const write = (start: string, end: string, resourceId: string | null, name: string | null = null) =>
    step('write_resource', start, end, 'saved', {
      resource: { resourceId, file: `${name ?? resourceId}.json`, name, kind: name === null ? null : 'entity' },
    })
  const resources = [
    {
      id: 'r-1',
      kind: 'page',
      name: 'RequestList',
      file: 'pages/list.json',
      version: 'v',
      usedBy: 0,
      titles: { en: 'Requests' },
    },
  ]

  it('reads writes that follow each other as one row that opens to each, in its own words, and how long each took', async () => {
    stub({
      ...fromApp,
      state: 'working',
      checks: null,
      aiConnected: true,
      steps: [
        step('propose_plan', '2026-11-10T09:00:00Z', '2026-11-10T09:00:40Z', null),
        write('2026-11-10T09:01:00Z', '2026-11-10T09:01:10Z', null, 'Cost centre'),
        write('2026-11-10T09:01:10Z', '2026-11-10T09:02:00Z', 'r-1'),
        write('2026-11-10T09:02:00Z', '2026-11-10T09:04:00Z', 'r-1'),
        step('validate_draft', '2026-11-10T09:04:00Z', null, null),
      ],
    })
    const onOpen = vi.fn()
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={resources}
        onOpen={onOpen}
      />,
    )
    const user = userEvent.setup()
    // Proposed: what AI does now reads under the item's title, the fold of its steps after it on its line.
    expect(await screen.findByTestId('studio-ai-now')).toHaveTextContent('Compiling the draft Show the steps')
    await user.click(screen.getByRole('button', { name: 'Show the steps' }))

    const rows = screen.getAllByRole('row').slice(1)
    expect(rows).toHaveLength(3)
    expect(rows[0]).toHaveTextContent(/Proposed a plan\s*Plan written.*40 s/)
    expect(rows[1]).toHaveTextContent(/Wrote 3 resources\s*Saved.*3 min/)
    // A step that runs reads as what it does now.
    expect(rows[2]).toHaveTextContent(/Compiling the draft\s*Running…/)

    // The table opens the group row to a row for each write, its resource named as people read it.
    await user.click(within(rows[1]).getByRole('button', { name: 'Show the rows in it' }))
    const opened = screen.getAllByRole('row').slice(1)
    expect(opened).toHaveLength(6)
    expect(opened[2]).toHaveTextContent(/Wrote Cost centre\s*Entity\s*Saved.*10 s/)
    await user.click(within(opened[3]).getByRole('button', { name: 'Requests' }))
    expect(onOpen).toHaveBeenCalledWith('r-1', '')
  })

  it("puts a check's results and why a step stopped its request in words", async () => {
    stub({
      ...fromApp,
      state: 'failed',
      steps: [
        step('check', '2026-11-10T09:00:00Z', '2026-11-10T09:01:00Z', 'failed', {
          checks: {
            compile: { outcome: 'passed', diagnostics: [] },
            scenarios: { outcome: 'failed', items: [{ outcome: 'passed' }, { outcome: 'failed' }] },
          },
        }),
        step('fix', '2026-11-10T09:01:00Z', '2026-11-10T09:02:00Z', null),
        step('check', '2026-11-10T09:02:00Z', '2026-11-10T09:03:00Z', 'passed', {
          checks: {
            compile: { outcome: 'passed', diagnostics: [] },
            scenarios: { outcome: 'passed', items: [{ outcome: 'passed' }, { outcome: 'passed' }] },
          },
        }),
        step('review', '2026-11-10T09:03:00Z', '2026-11-10T09:33:00Z', 'timedOut'),
      ],
    })
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={resources}
        onOpen={() => {}}
      />,
    )
    const user = userEvent.setup()
    // Proposed: what AI did reads under the item's title, Work, the fold of its steps after it on its line.
    const work = await screen.findByTestId('studio-ai-feed-work')
    expect(within(work).getByText('Work')).toBeInTheDocument()
    expect(within(work).getByTestId('studio-ai-steps-fold').parentElement).toHaveTextContent(
      '4 steps in 33 min Show the steps',
    )
    await user.click(within(work).getByRole('button', { name: 'Show the steps' }))

    const rows = screen.getAllByRole('row').slice(1)
    expect(rows[0]).toHaveTextContent(/Checked the draft\s*1 of 2 scenarios pass/)
    expect(rows[1]).toHaveTextContent(/Fixed the draft\s*Draft saved/)
    // Two scenarios that pass read as both.
    expect(rows[2]).toHaveTextContent(/Checked the draft\s*Both scenarios pass/)
    expect(rows[3]).toHaveTextContent(/Reviewed the draft\s*Ran out of time.*30 min/)
  })
})

// Proposed: a draft's changes name each resource as people read it, a resource only the draft adds by its label, see the
// review on #3263.
describe("a request's changes", () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it.each([
    ['a ready request, from its draft', { state: 'ready', draft: 'd-1' }],
    ['an accepted request, from the revision its accept made', { state: 'accepted', draft: null }],
  ])('names each changed resource by its label, a resource only the draft adds too: %s', async (_name, state) => {
    const detail = { ...fromApp, ...state }
    const diff = {
      cursor: null,
      resources: [
        {
          file: 'entities/cost-centre.json',
          change: 'added',
          id: null,
          kind: 'entity',
          name: 'CostCentre',
          before: null,
          after: '{"kind":"entity","name":"CostCentre"}',
          titles: { en: 'Cost centre' },
        },
      ],
    }
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string) => json(input.includes('/diff') ? diff : detail)),
    )
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )
    const file = await screen.findByTestId('studio-revision-change')
    expect(file).toHaveTextContent(/Cost centre\s*Entity\s*Added/)
  })
})

// Proposed: the bar at the foot of the window says what comes next, with one main action for it, see the review on #3263.
describe("a request's guide", () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  const renderPage = () =>
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={() => {}}
      />,
    )

  it('says AI works, with no main action', async () => {
    stub({ ...fromApp, state: 'working', checks: null, aiConnected: true })
    renderPage()

    const bar = await screen.findByTestId('studio-ai-bar')
    expect(bar).toHaveTextContent('AI is working on it')
    expect(
      within(bar)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Drop…'])
  })

  it('leads with asking AI to fix what the review found must change, and sends the findings', async () => {
    const fetchMock = stub({
      ...fromApp,
      aiConnected: true,
      review: {
        summary: 'The list shows no column.',
        findings: [
          { blocking: true, text: 'Show the column.', file: 'pages/list.json', path: '/widgets/0', resourceId: null },
          { blocking: false, text: 'Could say why.', file: 'pages/list.json', path: '/widgets/1', resourceId: null },
        ],
      },
    })
    renderPage()

    const bar = await screen.findByTestId('studio-ai-bar')
    expect(bar).toHaveTextContent('1 blocking finding')
    await userEvent.setup().click(within(bar).getByRole('button', { name: 'Ask AI to fix it' }))
    await waitFor(() => {
      const post = fetchMock.mock.calls.find(
        ([url, init]) => init?.method === 'POST' && url.endsWith('/request-changes'),
      )
      expect(post?.[1]?.body).toBe(
        JSON.stringify({ comment: 'Fix what the review found must change:\n- Show the column.' }),
      )
    })
  })

  it('asks before accepting past a must-change finding, and lists it', async () => {
    const fetchMock = stub({
      ...fromApp,
      aiConnected: true,
      review: {
        summary: 'The list shows no column.',
        findings: [
          { blocking: true, text: 'Show the column.', file: 'pages/list.json', path: '/widgets/0', resourceId: null },
          { blocking: false, text: 'Could say why.', file: 'pages/list.json', path: '/widgets/1', resourceId: null },
        ],
      },
    })
    renderPage()
    const user = userEvent.setup()

    await user.click(within(await screen.findByTestId('studio-ai-bar')).getByRole('button', { name: 'Accept anyway…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Accept without these changes?' })
    // The findings it leaves out read as the review lists them, under Blocking findings, with no non-blocking ones.
    const left = within(dialog).getByTestId('studio-ai-anyway-findings')
    expect(within(left).getByTestId('studio-ai-findings-blocking')).toHaveTextContent(
      /Blocking findings.*Show the column\./,
    )
    expect(within(left).queryByTestId('studio-ai-findings-non-blocking')).toBeNull()
    expect(fetchMock.mock.calls.some(([url]) => url.endsWith('/accept'))).toBe(false)
    await user.click(within(dialog).getByRole('button', { name: 'Accept anyway' }))
    await waitFor(() => {
      expect(fetchMock.mock.calls.some(([url, init]) => init?.method === 'POST' && url.endsWith('/accept'))).toBe(true)
    })
  })

  it('sends a failed request again as a new request', async () => {
    const fetchMock = stub({ ...fromApp, state: 'failed', checks: null, review: null, failure: 'No credit left.' })
    renderPage()

    const bar = await screen.findByTestId('studio-ai-bar')
    expect(bar).toHaveTextContent('AI could not finish')
    await userEvent.setup().click(within(bar).getByRole('button', { name: 'Send again' }))
    await waitFor(() => {
      const post = fetchMock.mock.calls.find(
        ([url, init]) => init?.method === 'POST' && url.endsWith('/authoring-requests'),
      )
      expect(post?.[1]?.body).toBe(JSON.stringify({ text: fromApp.text }))
    })
  })
})
