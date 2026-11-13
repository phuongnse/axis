import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioAuthoringRequestDetail } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
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
  app: 'Claude Code',
  coAuthors: ['Claude Code'],
  state: 'ready',
  plan: 'Add a Cost centre field to the purchase request.',
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
      summary: 'Checked the draft',
      outcome: 'No problems',
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
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        {children}
      </TextProvider>
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
    expect(within(screen.getByTestId('studio-ai-progress')).getByText('Ready')).toBeInTheDocument()
    expect(screen.getByTestId('studio-ai-plan')).toHaveTextContent('Add a Cost centre field')
    expect(screen.getByRole('button', { name: 'Accept' })).toBeInTheDocument()
    expect(screen.getByTestId('studio-ai-not-reviewed')).toHaveTextContent(
      'No second AI reviewed this change, as you have not connected your AI.',
    )
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
    // Every request goes through the same frame, whichever way it began.
    const progress = screen.getByTestId('studio-ai-progress')
    for (const stage of ['Plan', 'Approval', 'Change', 'Checks', 'Review', 'Ready']) {
      expect(within(progress).getByText(stage)).toBeInTheDocument()
    }
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

    expect(await screen.findByTestId('studio-ai-app-works')).toHaveTextContent(
      'Connect your AI to have AI go on here, or go on in Claude Code, and it reads what you asked here.',
    )
    expect(screen.getByTestId('studio-ai-progress').querySelector('.ant-spin')).toBeNull()
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

    await user.click(await screen.findByRole('button', { name: 'Ask for changes…' }))
    const dialog = await screen.findByRole('dialog', { name: 'Ask for changes' })
    await user.type(
      within(dialog).getByRole('textbox', { name: 'What should AI change?' }),
      'Make the cost centre optional.',
    )
    await user.click(within(dialog).getByRole('button', { name: 'Ask AI to change it' }))

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

  it('warns of blocking findings its fix rounds left open, then lists its notes, each at its file and path', async () => {
    stub({
      ...fromApp,
      aiConnected: true,
      review: [
        {
          blocking: true,
          text: 'Mails the approver too.',
          file: 'processes/p.json',
          path: '/steps/4/to/1',
          resourceId: null,
        },
        { blocking: false, text: 'Could say why.', file: 'processes/p.json', path: '/steps/5', resourceId: null },
      ],
    })
    renderPage()

    const review = await screen.findByTestId('studio-ai-review')
    expect(within(review).getByRole('alert')).toHaveTextContent('so it will not accept the change by itself')
    expect(within(review).getByText('Must change')).toBeInTheDocument()
    expect(within(review).getByText('Mails the approver too.')).toBeInTheDocument()
    expect(within(review).queryByText('error')).toBeNull()
    expect(within(review).getByText('Notes')).toBeInTheDocument()
    expect(within(review).getByText(/processes\/p\.json, .*\/steps\/5/)).toBeInTheDocument()
    expect(
      within(review).getByText('A review is no guarantee. Read the changes before you accept.'),
    ).toBeInTheDocument()
  })

  it('opens the resource of a finding at its path', async () => {
    stub({
      ...fromApp,
      aiConnected: true,
      review: [
        { blocking: false, text: 'Could say why.', file: 'pages/list.json', path: '/widgets/0', resourceId: 'r-1' },
      ],
    })
    const onOpen = vi.fn()
    renderIn(
      <StudioAuthoringRequestPage
        application={application}
        requestId={fromApp.id}
        userId={userId}
        resources={[]}
        onOpen={onOpen}
      />,
    )

    await userEvent.setup().click(await screen.findByRole('button', { name: /Could say why\./ }))

    expect(onOpen).toHaveBeenCalledWith('r-1', '/widgets/0')
  })

  it('says it found nothing that must change, and that a review is no guarantee', async () => {
    stub({ ...fromApp, aiConnected: true, review: [] })
    renderPage()

    const review = await screen.findByTestId('studio-ai-review')
    expect(within(review).getByText('The review found nothing that must change.')).toBeInTheDocument()
    expect(within(review).queryByRole('alert')).toBeNull()
    expect(
      within(review).getByText('A review is no guarantee. Read the changes before you accept.'),
    ).toBeInTheDocument()
  })
})

// Proposed: a ready request's page puts what its author decides on first, and the detail in tabs, see the review on #3263.
describe('a ready request’s page', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('leads with its checks and review, lists what changes, and opens a change in the Changes tab', async () => {
    const fetchMock = vi.fn(async (input: string) =>
      input.includes('/drafts/')
        ? json({
            start: 'a',
            tip: 'b',
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
              },
            ],
          })
        : json({
            ...fromApp,
            aiConnected: true,
            draft: 'd-1',
            review: [],
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
    expect(within(checks).getByText('It compiles, and 1 of 1 scenarios pass.')).toBeInTheDocument()
    expect(screen.getByText('Checks and review passed')).toBeInTheDocument()
    const changeList = await screen.findByTestId('studio-ai-what-changes')
    expect(within(changeList).getByText('A cost centre with a name and a code.')).toBeInTheDocument()

    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    await userEvent.setup().click(within(changeList).getByRole('button', { name: 'CostCentre' }))

    expect(await screen.findByRole('tab', { name: 'Changes (1)', selected: true })).toBeInTheDocument()
    await waitFor(() => expect(scrollIntoView).toHaveBeenCalled())
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
