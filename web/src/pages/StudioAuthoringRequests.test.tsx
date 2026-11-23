import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { AuthoringRequest, AuthoringRequestState } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioAuthoringRequests } from './StudioAuthoringRequests'

const application = 'PurchaseRequests'
const path = `/api/studio/apps/${application}/authoring-requests`

const request = (
  id: string,
  state: AuthoringRequestState,
  overrides: Partial<AuthoringRequest> = {},
): AuthoringRequest => ({
  id,
  application,
  author: 'u1',
  authorName: 'Linh Nguyen',
  text: 'Add a cost centre to every purchase request.',
  title: null,
  state,
  plan: null,
  round: 0,
  tokens: 0,
  autoAccept: false,
  sentAt: '2026-11-02T09:30:00Z',
  approvedAt: null,
  finishedAt: null,
  draft: null,
  ...overrides,
})

/** A page of the requests: `items` is the page and `totalCount` how many match in all. */
function answer(items: AuthoringRequest[], totalCount: number) {
  return new Response(JSON.stringify({ items, page: 1, pageSize: 20, totalCount }), {
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Answers the requests with `reply`, and `/api/me` as a person who may not administer users. */
function stubRequests(reply: (url: string) => Response) {
  const fetchMock = vi.fn(async (input: string) =>
    input.startsWith(path)
      ? reply(input)
      : input === '/api/me'
        ? new Response(JSON.stringify({ id: 'author', users: false }), {
            headers: { 'Content-Type': 'application/json' },
          })
        : new Response(null, { status: 404 }),
  )
  vi.stubGlobal('fetch', fetchMock)
  // The table's own choices of columns and views, and the signed-in person, are not a request for its rows.
  return () =>
    fetchMock.mock.calls
      .map(([input]) => input)
      .filter((input) => input !== '/api/me' && !input.startsWith('/api/me/tables/'))
}

const Location = () => <output data-testid="location">{useLocation().search}</output>

function renderRequests(address = '/studio?app=PurchaseRequests&view=requests') {
  render(
    <MemoryRouter initialEntries={[address]}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioAuthoringRequests application={application} />
          <Location />
        </TextProvider>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('StudioAuthoringRequests', () => {
  it('shows a Request, State, Sent by and Sent column', async () => {
    stubRequests(() => answer([request('r1', 'awaitingApproval', { title: 'Cost centre' })], 1))
    renderRequests()

    expect(await screen.findByText('Cost centre')).toBeVisible()
    for (const name of ['Request', 'State', 'Sent by', 'Sent']) {
      expect(screen.getByRole('columnheader', { name })).toBeVisible()
    }
  })

  it('shows each state as a status tag with its word, and a fix round as Working', async () => {
    stubRequests(() =>
      answer(
        [
          request('r1', 'awaitingApproval', { title: 'Cost centre' }),
          request('r2', 'fixing', { title: 'Supplier field' }),
        ],
        2,
      ),
    )
    renderRequests()

    const [waiting, fixing] = await screen.findAllByTestId('studio-ai-requests-state')
    expect(waiting).toHaveTextContent('Awaiting approval')
    expect(waiting).toHaveAttribute('data-tone', 'attention')
    expect(fixing).toHaveTextContent('Working')
    expect(fixing).toHaveAttribute('data-tone', 'neutral')
  })

  it('names an untitled request by its first sentence and links it to its page', async () => {
    stubRequests(() => answer([request('r3', 'planning', { text: 'Add a supplier field. Then keep it required.' })], 1))
    renderRequests()

    const link = await screen.findByRole('link', { name: 'Add a supplier field.' })
    expect(link).toHaveAttribute('href', '/studio?app=PurchaseRequests&view=requests&request=r3')
  })

  it('asks the server for the search, the filter, the sort and the page of the address', async () => {
    const filter = JSON.stringify({ all: [{ field: 'state', op: 'eq', value: 'working' }] })
    const requested = stubRequests(() => answer([request('r1', 'working', { title: 'Cost centre' })], 1))

    renderRequests(
      `/studio?app=PurchaseRequests&view=requests&search=cost&filter=${encodeURIComponent(filter)}&sort=-state&page=2&pageSize=50`,
    )

    expect(await screen.findByText('Cost centre')).toBeVisible()
    const [url] = requested()
    const query = new URLSearchParams(url.slice(path.length + 1))
    expect(query.get('search')).toBe('cost')
    expect(query.get('filter')).toBe(filter)
    expect(query.get('sort')).toBe('-state')
    expect(query.get('page')).toBe('2')
    expect(query.get('pageSize')).toBe('50')
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('cost')
  })

  it('keeps a search typed in the Search box in the address and asks the server again', async () => {
    const requested = stubRequests(() => answer([request('r1', 'working', { title: 'Cost centre' })], 1))
    renderRequests()
    await screen.findByText('Cost centre')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'report{Enter}')

    await vi.waitFor(() => expect(requested()).toContain(`${path}?search=report`))
    expect(screen.getByTestId('location')).toHaveTextContent('search=report')
  })
})
