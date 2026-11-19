import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, useLocation } from 'react-router'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { ConsoleApplication, ConsolePublishing as Publishing } from './console'
import { ConsolePublishing } from './ConsolePublishing'
import { TextProvider } from './TextProvider'

const applications: ConsoleApplication[] = [
  {
    id: 'a1',
    name: 'Leave',
    releaseId: 'r2',
    releaseNumber: 2,
    releaseLabel: null,
    activatedAt: '2026-10-28T09:00:00Z',
  },
  {
    id: 'a2',
    name: 'Purchasing',
    releaseId: 'p1',
    releaseNumber: 1,
    releaseLabel: null,
    activatedAt: '2026-10-29T09:00:00Z',
  },
]

function release(releaseId: string, number: number, overrides: Partial<Publishing['releases'][number]> = {}) {
  return {
    releaseId,
    number,
    contentHash: `hash-${releaseId}`,
    activatedAt: '2026-10-28T09:00:00Z',
    actor: 'u1',
    actorName: null,
    label: null,
    note: null,
    requestId: null,
    offStandard: false,
    relaxed: [],
    ...overrides,
  }
}

function request(id: string, overrides: Partial<Publishing['requests'][number]> = {}) {
  return {
    id,
    kind: 'publish' as const,
    target: 'tenant',
    status: 'succeeded' as const,
    label: null,
    note: null,
    requestedBy: 'u1',
    requestedByName: null,
    requestedAt: '2026-10-28T08:00:00Z',
    startedAt: '2026-10-28T08:00:01Z',
    finishedAt: '2026-10-28T08:00:09Z',
    releaseId: null,
    ...overrides,
  }
}

const leave: Publishing = {
  active: { releaseId: 'r2', number: 2, contentHash: 'hash-r2' },
  releases: [
    // The active release is in the history twice, after an activation, and only the newest row is marked.
    release('r2', 2, { actorName: 'Rita Reader', label: 'Spring', note: 'Back again' }),
    release('r1', 1, { actor: 'startup', activatedAt: '2026-10-27T10:00:00Z' }),
    release('r2', 2, { actor: 'operator', activatedAt: '2026-10-27T09:00:00Z' }),
  ],
  requests: [
    request('q2', { kind: 'activate', status: 'failed', label: 'Undo', requestedByName: 'Alan Author' }),
    request('q1', { requestedBy: 'u9' }),
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
}

const purchasing: Publishing = {
  active: { releaseId: 'p1', number: 1, contentHash: 'hash-p1' },
  releases: [release('p1', 1, { label: 'Initial' })],
  requests: [
    request('q3', { target: 'preview', status: 'queued', finishedAt: null, requestedByName: 'Pia Publisher' }),
  ],
  page: 1,
  pageSize: 20,
  totalCount: 1,
}

function stubServer(answer: (url: string) => Response) {
  const calls: { url: string; method: string }[] = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init?: RequestInit) => {
      // The table's own choices of columns and the signed-in person are not a request for its rows.
      if (input !== '/api/me' && !input.startsWith('/api/me/tables/')) {
        calls.push({ url: input, method: init?.method ?? 'GET' })
      }
      return answer(input)
    }),
  )
  return calls
}

function answer(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

const Location = () => <output data-testid="location">{useLocation().search}</output>

function renderView(address = '/console') {
  render(
    <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
      <MemoryRouter initialEntries={[address]}>
        <ConsolePublishing applications={applications} locale="en" />
        <Location />
      </MemoryRouter>
    </TextProvider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('ConsolePublishing', () => {
  it('lists the releases and the publish requests of the first application, with the active release marked', async () => {
    const calls = stubServer(() => answer(leave))
    renderView()

    const releases = await screen.findByTestId('console-publishing-releases')
    expect(await within(releases).findByText('#1')).toBeVisible()
    expect(calls).toEqual([{ url: '/api/console/applications/a1/publishing?', method: 'GET' }])
    for (const title of ['Release', 'Label', 'Note', 'Activated by', 'Activated', 'Relaxed points']) {
      expect(within(releases).getByRole('columnheader', { name: title })).toBeVisible()
    }
    const rows = within(releases).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(3)
    expect(within(rows[0]!).getByText('#2')).toBeVisible()
    expect(within(rows[0]!).getByText('Spring')).toBeVisible()
    expect(within(rows[0]!).getByText('Back again')).toBeVisible()
    expect(within(rows[0]!).getByText('Rita Reader')).toBeVisible()
    expect(within(rows[1]!).getByText('System')).toBeVisible()
    expect(within(releases).getAllByText('Live')).toHaveLength(1)
    expect(within(rows[0]!).getByText('Live')).toBeVisible()
    expect(within(rows[0]!).getByText('Live').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')

    const requests = screen.getByTestId('console-publishing-requests')
    const requestRows = within(requests).getAllByRole('row').slice(1)
    expect(requestRows).toHaveLength(2)
    expect(within(requestRows[0]!).getByText('activate')).toBeVisible()
    expect(within(requestRows[0]!).getByText('Failed')).toBeVisible()
    expect(within(requestRows[0]!).getByText('Failed').closest('[data-tone]')).toHaveAttribute('data-tone', 'danger')
    expect(within(requestRows[0]!).getByText('Undo')).toBeVisible()
    expect(within(requestRows[0]!).getByText('Alan Author')).toBeVisible()
    expect(within(requestRows[1]!).getByText('u9')).toBeVisible()
    expect(within(requestRows[1]!).getByText('Succeeded')).toBeVisible()
    expect(within(requestRows[1]!).getByText('Succeeded').closest('[data-tone]')).toHaveAttribute(
      'data-tone',
      'success',
    )
  })

  it('names who activated each release in words, and never shows an id that has no name', async () => {
    stubServer(() =>
      answer({
        ...leave,
        releases: [
          release('r3', 3, { actor: 'startup' }),
          release('r2', 2, { actor: 'operator', activatedAt: '2026-10-27T10:00:00Z' }),
          release('r1', 1, { actor: 'u7', activatedAt: '2026-10-27T09:00:00Z' }),
        ],
      }),
    )
    renderView()

    const releases = await screen.findByTestId('console-publishing-releases')
    expect(await within(releases).findByText('#3')).toBeVisible()
    const rows = within(releases).getAllByRole('row').slice(1)
    expect(within(rows[0]!).getByText('System')).toBeVisible()
    expect(within(rows[1]!).getByText('The platform operator')).toBeVisible()
    expect(within(rows[2]!).getByText('Someone no longer known')).toBeVisible()
    expect(within(releases).queryByText('u7')).toBeNull()
  })

  it('marks an off-standard release with the tag, its points and reasons, and leaves a standard release unmarked', async () => {
    stubServer(() =>
      answer({
        ...leave,
        releases: [
          release('r2', 2, {
            offStandard: true,
            relaxed: [
              {
                module: 'department-budgets',
                point: 'Budget/transitions/approve',
                reason: 'Approvers decide alone',
                moduleTitle: null,
                part: null,
                entityTitles: null,
                titles: null,
              },
              {
                module: 'department-budgets',
                point: 'Budget/fields/limit',
                reason: 'Pilot',
                moduleTitle: null,
                part: null,
                entityTitles: null,
                titles: null,
              },
            ],
          }),
          release('r1', 1, { activatedAt: '2026-10-27T10:00:00Z' }),
        ],
      }),
    )
    renderView()

    const releases = await screen.findByTestId('console-publishing-releases')
    expect(await within(releases).findByText('#1')).toBeVisible()
    const rows = within(releases).getAllByRole('row').slice(1)
    expect(within(rows[0]!).getByTestId('release-off-standard')).toHaveTextContent('Off-standard')
    const points = within(rows[0]!).getAllByTestId('release-relaxed-point')
    expect(points).toHaveLength(2)
    expect(points[0]).toHaveTextContent('department-budgets')
    expect(points[0]).toHaveTextContent('Budget/fields/limit')
    expect(points[0]).toHaveTextContent('Pilot')
    expect(points[1]).toHaveTextContent('Budget/transitions/approve')
    expect(points[1]).toHaveTextContent('Approvers decide alone')
    expect(within(rows[1]!).queryByTestId('release-off-standard')).toBeNull()
    expect(within(rows[1]!).queryByTestId('release-relaxed-point')).toBeNull()
  })

  it('names the people of the history, linking them to the user page for an administrator, and opens a release in Studio', async () => {
    stubServer((url) => (url === '/api/me' ? answer({ id: 'admin', users: true, studio: true }) : answer(leave)))
    renderView()

    const releases = await screen.findByTestId('console-publishing-releases')
    expect(await within(releases).findByRole('link', { name: 'Rita Reader' })).toHaveAttribute('href', '/users/u1')
    // The newest row of release 2 is the active one, and the system's and the operator's activations stay plain words.
    expect(within(releases).getAllByRole('link', { name: '#2' })[0]).toHaveAttribute(
      'href',
      '/studio?app=Leave&view=release&release=2',
    )
    expect(within(releases).getByRole('link', { name: '#1' })).toHaveAttribute(
      'href',
      '/studio?app=Leave&view=release&release=1',
    )
    expect(within(releases).getByText('System')).toBeVisible()
    expect(within(releases).getByText('The platform operator')).toBeVisible()
    expect(within(releases).queryByRole('link', { name: 'System' })).toBeNull()
    const requests = screen.getByTestId('console-publishing-requests')
    expect(within(requests).getByRole('link', { name: 'Alan Author' })).toHaveAttribute('href', '/users/u1')
  })

  it('shows the people and the release numbers as plain text for a reader who may neither administer users nor use Studio', async () => {
    const calls = stubServer((url) =>
      url === '/api/me' ? answer({ id: 'reader', users: false, studio: false }) : answer(leave),
    )
    renderView()

    const view = await screen.findByTestId('console-publishing')
    expect(await within(view).findByText('#1')).toBeVisible()
    expect(within(view).getByText('Rita Reader')).toBeVisible()
    await waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/me', expect.anything()))
    expect(within(view).queryByRole('link')).toBeNull()
    expect(calls).toHaveLength(1)
  })

  it('loads the publishing of the application that is chosen', async () => {
    const calls = stubServer((url) => answer(url.includes('/a2/') ? purchasing : leave))
    renderView()
    const view = await screen.findByTestId('console-publishing')
    await within(view).findByText('Back again')

    await userEvent.click(within(view).getByRole('combobox', { name: 'Application' }))
    await userEvent.click(await screen.findByTitle('Purchasing'))

    expect(await within(view).findByText('Initial')).toBeVisible()
    expect(calls.at(-1)).toEqual({ url: '/api/console/applications/a2/publishing?', method: 'GET' })
    expect(within(view).getByText('Pia Publisher')).toBeVisible()
    expect(within(view).getByText('Queued')).toBeVisible()
    expect(within(view).getByText('Queued').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
    expect(within(view).queryByText('Back again')).toBeNull()
    expect(within(view).getAllByText('Live')).toHaveLength(1)
  })

  it('keeps the search, filter and sort of the publish requests in the address and sends them', async () => {
    const filter = JSON.stringify({ all: [{ field: 'status', op: 'eq', value: 'failed' }] })
    const address = `/console?${new URLSearchParams({
      'publishRequests.search': 'Undo',
      'publishRequests.filter': filter,
      'publishRequests.sort': '-requestedAt',
    })}`
    const calls = stubServer(() => answer({ ...leave, totalCount: 45 }))
    const user = userEvent.setup()
    renderView(address)

    const requests = await screen.findByTestId('console-publishing-requests')
    expect(await within(requests).findByText('1–20 of 45')).toBeVisible()
    const first = new URL(calls.at(-1)!.url, 'http://localhost').searchParams
    expect(first.get('search')).toBe('Undo')
    expect(first.get('filter')).toBe(filter)
    expect(first.get('sort')).toBe('-requestedAt')

    // The page size picker of the requests table, not the Application picker.
    await user.click(within(requests).getByRole('combobox'))
    await user.click(await screen.findByTitle('50 / page'))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('publishRequests.pageSize=50'))
    await waitFor(() => expect(calls.at(-1)?.url).toContain('pageSize=50'))
    expect(screen.getByTestId('location')).toHaveTextContent('publishRequests.sort=-requestedAt')
    expect(calls.at(-1)?.url).toContain('sort=-requestedAt')
  })

  it('offers no publish or activate command and sends nothing but GET requests', async () => {
    const calls = stubServer(() => answer(leave))
    renderView()
    const view = await screen.findByTestId('console-publishing')
    await within(view).findByText('Back again')

    expect(within(view).queryByRole('button', { name: /publish|activate|roll ?back|redeliver|discard/i })).toBeNull()
    expect(calls.every((call) => call.method === 'GET')).toBe(true)
  })

  it.each([
    ['cannot be loaded', 500],
    ['is forbidden', 403],
  ])('shows the error in place of the rows of each table when the publishing %s', async (_, status) => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(() => new Response(null, { status }))
    renderView()

    const releases = await screen.findByTestId('console-publishing-releases')
    const releasesError = await within(releases).findByTestId('console-publishing-releases-error')
    expect(releasesError).toHaveTextContent('The publishing could not be loaded.')
    expect(within(releases).queryByText('Nothing to show.')).toBeNull()

    const requests = screen.getByTestId('console-publishing-requests')
    const requestsError = within(requests).getByTestId('console-publishing-requests-error')
    expect(requestsError).toHaveTextContent('The publishing could not be loaded.')
    expect(within(requests).queryByText('Nothing to show.')).toBeNull()
    // The toolbar stays, so the person can still search, filter and refresh.
    expect(within(requests).getByRole('searchbox', { name: 'Search' })).toBeVisible()
    expect(within(requests).getByRole('button', { name: 'Refresh' })).toBeVisible()
  })
})
