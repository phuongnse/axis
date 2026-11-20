import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { StudioRevision } from '../platform/studio'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { StudioHistory } from './StudioHistory'

const application = 'PurchaseRequests'
const path = `/api/studio/apps/${application}/revisions`

const revision = (id: string, summary: string, author = 'Linh Nguyen'): StudioRevision => ({
  id,
  summary,
  author,
  userId: 'u1',
  client: 'studio',
  request: null,
  time: '2026-11-02T09:30:00Z',
})

/** A page of the history: `items` is the page, `totalCount` how many match, `start` the id of the start of history. */
function answer(items: StudioRevision[], totalCount: number, start: string | null) {
  return new Response(JSON.stringify({ items, page: 1, pageSize: 20, totalCount, start }), {
    headers: { 'Content-Type': 'application/json' },
  })
}

/** Answers the history with `reply`, and `/api/me` with what `me` says: whether the reader may administer users. */
function stubRevisions(reply: (url: string) => Response, users = false) {
  const fetchMock = vi.fn(async (input: string) =>
    input.startsWith(path)
      ? reply(input)
      : input === '/api/me'
        ? new Response(JSON.stringify({ id: 'admin', users }), { headers: { 'Content-Type': 'application/json' } })
        : new Response(null, { status: 404 }),
  )
  vi.stubGlobal('fetch', fetchMock)
  // The table's own choices of columns and the signed-in person are not a request for its rows.
  return () =>
    fetchMock.mock.calls
      .map(([input]) => input)
      .filter((input) => input !== '/api/me' && !input.startsWith('/api/me/tables/'))
}

const Location = () => <output data-testid="location">{useLocation().search}</output>

function renderHistory(address = '/studio?app=PurchaseRequests&view=history') {
  render(
    <MemoryRouter initialEntries={[address]}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <StudioHistory application={application} folderRevision={1} onUndone={vi.fn()} />
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

describe('StudioHistory', () => {
  it('asks the server for the search, the filter and the sort of the address and shows the rows it answers', async () => {
    const filter = JSON.stringify({ all: [{ field: 'author', op: 'contains', value: 'linh' }] })
    const requested = stubRevisions(() => answer([revision('r7', 'Added a supplier field')], 1, 'r1'))

    renderHistory(
      `/studio?app=PurchaseRequests&view=history&search=supplier&filter=${encodeURIComponent(filter)}&sort=-time`,
    )

    expect(await screen.findByText('Added a supplier field')).toBeVisible()
    const [url] = requested()
    const query = new URLSearchParams(url.slice(path.length + 1))
    expect(query.get('search')).toBe('supplier')
    expect(query.get('filter')).toBe(filter)
    expect(query.get('sort')).toBe('-time')
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('supplier')
  })

  it('keeps a search typed in the Search box in the address and asks the server again', async () => {
    const requested = stubRevisions(() => answer([revision('r7', 'Added a supplier field')], 1, 'r1'))
    renderHistory()
    await screen.findByText('Added a supplier field')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'supplier{Enter}')

    await vi.waitFor(() => expect(requested()).toContain(`${path}?search=supplier`))
    expect(screen.getByTestId('location')).toHaveTextContent('search=supplier')
  })

  it('sorts from the header of Who and sends the sort to the server', async () => {
    const requested = stubRevisions(() => answer([revision('r7', 'Added a supplier field')], 1, 'r1'))
    renderHistory()
    await screen.findByText('Added a supplier field')

    await userEvent.click(screen.getByRole('columnheader', { name: 'Who' }))

    await vi.waitFor(() => expect(requested()).toContain(`${path}?sort=author`))
    expect(screen.getByTestId('location')).toHaveTextContent('sort=author')
  })

  it('has no sort on the Summary header', async () => {
    const requested = stubRevisions(() => answer([revision('r7', 'Added a supplier field')], 1, 'r1'))
    renderHistory()
    await screen.findByText('Added a supplier field')

    await userEvent.click(screen.getByRole('columnheader', { name: 'Summary' }))
    await new Promise((resolve) => setTimeout(resolve, 50))

    expect(requested()).toEqual([path])
    expect(screen.getByTestId('location')).not.toHaveTextContent('sort=')
  })

  it('reads the range of the page and keeps a page size in the address', async () => {
    const page = Array.from({ length: 20 }, (_, index) => revision(`r${45 - index}`, `Change ${45 - index}`))
    const requested = stubRevisions(() => answer(page, 45, 'r1'))
    renderHistory()

    expect(await screen.findByText('1–20 of 45')).toBeVisible()
    await userEvent.click(screen.getByRole('combobox'))
    await userEvent.click(await screen.findByTitle('50 / page'))

    await vi.waitFor(() => expect(requested()).toContain(`${path}?pageSize=50`))
    expect(screen.getByTestId('location')).toHaveTextContent('pageSize=50')
  })

  it('asks the server again when Refresh is pressed', async () => {
    const requested = stubRevisions(() => answer([revision('r7', 'Added a supplier field')], 1, 'r1'))
    renderHistory()
    await screen.findByText('Added a supplier field')

    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await vi.waitFor(() => expect(requested()).toHaveLength(2))
  })

  it('links an author to the user page in the Who column for a reader who may administer users', async () => {
    stubRevisions(() => answer([revision('r2', 'Second'), revision('r1', 'First')], 2, 'r1'), true)
    renderHistory()

    const links = await screen.findAllByRole('link', { name: 'Linh Nguyen' })
    expect(links).toHaveLength(2)
    expect(links[0]).toHaveAttribute('href', '/users/u1')
  })

  it('shows an author as plain text in the Who column for a reader who may not administer users', async () => {
    const requested = stubRevisions(() => answer([revision('r2', 'Second')], 1, 'r1'), false)
    renderHistory()

    expect(await screen.findByText('Linh Nguyen')).toBeVisible()
    await vi.waitFor(() => expect(fetch).toHaveBeenCalledWith('/api/me', expect.anything()))
    expect(screen.queryByRole('link', { name: 'Linh Nguyen' })).not.toBeInTheDocument()
    expect(requested()).toEqual([path])
  })

  it('names the signed-in author with "(you)" and a revision the server made as System, with no link', async () => {
    stubRevisions(
      () => answer([revision('r2', 'Second'), { ...revision('r1', 'First', 'Axis'), userId: null }], 2, null),
      true,
    )
    renderHistory()

    await screen.findByText('Second')
    await vi.waitFor(() => expect(screen.getAllByRole('link', { name: 'Linh Nguyen' })).toHaveLength(1))
    expect(screen.getByText('System')).toBeVisible()
    expect(screen.queryByRole('link', { name: 'System' })).not.toBeInTheDocument()
  })

  it('gives every revision an Undo button except the one the server names as the start of history', async () => {
    stubRevisions(() => answer([revision('r3', 'Third'), revision('r2', 'Second'), revision('r1', 'First')], 3, 'r1'))
    renderHistory()
    await screen.findByText('Third')

    const rows = screen.getAllByTestId('studio-revision-row')
    const undoOf = (row: HTMLElement) => within(row).queryByRole('button', { name: /^Undo/ })
    expect(rows.map((row) => row.getAttribute('data-id'))).toEqual(['r3', 'r2', 'r1'])
    expect(undoOf(rows[0])).not.toBeNull()
    expect(undoOf(rows[1])).not.toBeNull()
    expect(undoOf(rows[2])).toBeNull()
  })

  it('keeps Undo on the last row of a page that does not hold the start of history', async () => {
    stubRevisions(() => answer([revision('r30', 'Thirtieth'), revision('r29', 'Twenty-ninth')], 30, 'r1'))
    renderHistory()
    await screen.findByText('Thirtieth')

    expect(screen.getAllByRole('button', { name: /^Undo/ })).toHaveLength(2)
  })
})
