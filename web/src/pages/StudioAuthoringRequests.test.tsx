import { render, screen, within } from '@testing-library/react'
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
  state: 'ready',
  plan: null,
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
  checks: null,
  review: null,
  changes: [],
  failure: null,
  accept: null,
}

function json(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function stub(answer: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => json(answer)),
  )
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

  it('names the app after the person in the list of AI requests', async () => {
    stub({ items: [fromApp] })
    renderIn(<StudioAuthoringRequests application={application} userId={userId} />)

    expect(await screen.findByText('You, from Claude Code')).toBeInTheDocument()
  })

  it('shows who sent it from which app, the stages of its own and Accept, with no plan and no review', async () => {
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

    expect(await screen.findByText(/^Sent by you from Claude Code on /)).toBeInTheDocument()
    const progress = screen.getByTestId('studio-ai-progress')
    expect(within(progress).getByText('Change')).toBeInTheDocument()
    expect(within(progress).getByText('Ready')).toBeInTheDocument()
    expect(within(progress).queryByText('Plan')).toBeNull()
    expect(within(progress).queryByText('Review')).toBeNull()
    expect(screen.queryByTestId('studio-ai-plan')).toBeNull()
    expect(screen.getByRole('button', { name: 'Accept' })).toBeInTheDocument()
  })
})
