import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
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
    // The active release is in the history twice, after a rollback, and only the newest row is marked.
    release('r2', 2, { actorName: 'Rita Reader', label: 'Spring', note: 'Back again' }),
    release('r1', 1, { actor: 'startup', activatedAt: '2026-10-27T10:00:00Z' }),
    release('r2', 2, { actor: 'operator', activatedAt: '2026-10-27T09:00:00Z' }),
  ],
  requests: [
    request('q2', { kind: 'rollback', status: 'failed', label: 'Undo', requestedByName: 'Alan Author' }),
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
      calls.push({ url: input, method: init?.method ?? 'GET' })
      return answer(input)
    }),
  )
  return calls
}

function answer(body: unknown) {
  return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
}

function renderView() {
  render(
    <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
      <ConsolePublishing applications={applications} locale="en" />
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
    expect(calls).toEqual([{ url: '/api/console/applications/a1/publishing?page=1&pageSize=20', method: 'GET' }])
    for (const title of ['Release', 'Label', 'Note', 'Activated by', 'Activated', 'Relaxed points']) {
      expect(within(releases).getByRole('columnheader', { name: title })).toBeVisible()
    }
    const rows = within(releases).getAllByRole('row').slice(1)
    expect(rows).toHaveLength(3)
    expect(within(rows[0]!).getByText('#2')).toBeVisible()
    expect(within(rows[0]!).getByText('Spring')).toBeVisible()
    expect(within(rows[0]!).getByText('Back again')).toBeVisible()
    expect(within(rows[0]!).getByText('Rita Reader')).toBeVisible()
    expect(within(rows[1]!).getByText('startup')).toBeVisible()
    expect(within(releases).getAllByText('Active')).toHaveLength(1)
    expect(within(rows[0]!).getByText('Active')).toBeVisible()

    const requests = screen.getByTestId('console-publishing-requests')
    const requestRows = within(requests).getAllByRole('row').slice(1)
    expect(requestRows).toHaveLength(2)
    expect(within(requestRows[0]!).getByText('rollback')).toBeVisible()
    expect(within(requestRows[0]!).getByText('Failed')).toBeVisible()
    expect(within(requestRows[0]!).getByText('Undo')).toBeVisible()
    expect(within(requestRows[0]!).getByText('Alan Author')).toBeVisible()
    expect(within(requestRows[1]!).getByText('u9')).toBeVisible()
    expect(within(requestRows[1]!).getByText('Succeeded')).toBeVisible()
  })

  it('marks an off-standard release with the tag, its points and reasons, and leaves a standard release unmarked', async () => {
    stubServer(() =>
      answer({
        ...leave,
        releases: [
          release('r2', 2, {
            offStandard: true,
            relaxed: [
              { module: 'department-budgets', point: 'Budget/transitions/approve', reason: 'Approvers decide alone' },
              { module: 'department-budgets', point: 'Budget/fields/limit', reason: 'Pilot' },
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
    expect(points[0]).toHaveTextContent('Budget/transitions/approve')
    expect(points[0]).toHaveTextContent('Approvers decide alone')
    expect(points[1]).toHaveTextContent('Pilot')
    expect(within(rows[1]!).queryByTestId('release-off-standard')).toBeNull()
    expect(within(rows[1]!).queryByTestId('release-relaxed-point')).toBeNull()
  })

  it('loads the publishing of the application that is chosen', async () => {
    const calls = stubServer((url) => answer(url.includes('/a2/') ? purchasing : leave))
    renderView()
    const view = await screen.findByTestId('console-publishing')
    await within(view).findByText('Back again')

    await userEvent.click(within(view).getByRole('combobox', { name: 'Application' }))
    await userEvent.click(await screen.findByTitle('Purchasing'))

    expect(await within(view).findByText('Initial')).toBeVisible()
    expect(calls.at(-1)).toEqual({ url: '/api/console/applications/a2/publishing?page=1&pageSize=20', method: 'GET' })
    expect(within(view).getByText('Pia Publisher')).toBeVisible()
    expect(within(view).getByText('Queued')).toBeVisible()
    expect(within(view).queryByText('Back again')).toBeNull()
    expect(within(view).getAllByText('Active')).toHaveLength(1)
  })

  it('requests the next page of the publish requests', async () => {
    const calls = stubServer(() => answer({ ...leave, totalCount: 45 }))
    renderView()
    const view = await screen.findByTestId('console-publishing')
    await within(view).findByText('Back again')

    await userEvent.click(within(view).getByTitle('2'))

    await waitFor(() =>
      expect(calls.at(-1)).toEqual({
        url: '/api/console/applications/a1/publishing?page=2&pageSize=20',
        method: 'GET',
      }),
    )
  })

  it('offers no command and sends nothing but GET requests', async () => {
    const calls = stubServer(() => answer(leave))
    renderView()
    const view = await screen.findByTestId('console-publishing')
    await within(view).findByText('Back again')

    expect(within(view).queryByRole('button', { name: /publish|activate|roll ?back|redeliver|discard/i })).toBeNull()
    expect(within(view).queryAllByRole('button')).toHaveLength(0)
    expect(calls.every((call) => call.method === 'GET')).toBe(true)
  })

  it('shows an error in the card when the publishing cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(() => new Response(null, { status: 500 }))
    renderView()

    const error = await screen.findByTestId('console-publishing-error')
    expect(within(error).getByText('The publishing could not be loaded.')).toBeVisible()
  })
})
