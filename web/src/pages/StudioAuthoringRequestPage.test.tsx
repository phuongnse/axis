import { act, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { formatValue } from '../platform/formatValue'
import type { AuthoringRequest, AuthoringRequestState } from '../platform/studio'
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

function renderPage(props: { onAskAi?: () => void } = {}) {
  render(
    <ThemeModeProvider>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <StudioAuthoringRequestPage application={application} requestId="r1" userId="u1" onAskAi={props.onAskAi} />
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
    expect(await screen.findByRole('heading', { level: 4, name })).toBeVisible()
    expect(screen.getByText(text)).toBeVisible()
    const time = formatValue({ name: 'sentAt', type: 'date-time' }, sentAt, {}, { locale: 'en', text: (key) => key })
    expect(screen.getByText(`Sent by Linh Nguyen on ${time}`)).toBeVisible()
  })

  it("names the sender as you when the request is the reader's own", async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => answer(request({ author: 'u1' }))),
    )
    renderPage()

    const time = formatValue({ name: 'sentAt', type: 'date-time' }, sentAt, {}, { locale: 'en', text: (key) => key })
    expect(await screen.findByText(`Sent by you on ${time}`)).toBeVisible()
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
