import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { formatValue } from './formatValue'
import { RecordPagesContext } from './recordPages'
import { isSignInNeeded } from './signInNeeded'
import { assignLocation } from './navigation'
import type { WidgetMetadata } from './site'
import { TaskInboxWidget } from './TaskInboxWidget'
import { TextProvider } from './TextProvider'
import type { TextCatalog } from './texts'

vi.mock('./navigation')

const tasksPath = '/api/apps/E2eApp/tasks'

const inboxWidget: WidgetMetadata = {
  type: 'taskInbox',
  entity: null,
  dataSource: null,
  form: null,
  tasksPath,
  process: null,
  instancesPath: null,
  deadLettersPath: null,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  priorities: {},
  access: null,
  detail: null,
  kpi: null,
  chart: null,
}

const reviewId = '6f1c2a3b-4d5e-4f60-8a71-92b3c4d5e6f7'
const approveId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'
const releaseId = '0192f4a0-1b2c-7d3e-8f40-5b6c7d8e9f02'
const dueAt = '2026-10-13T09:30:00.123456Z'

// In the task API's order: the earliest due date first, and no due date last.
const taskPage = JSON.stringify({
  items: [
    {
      id: reviewId,
      releaseId,
      process: 'NoteReview',
      step: 'review',
      labelKey: 'noteReview.task',
      subject: { entity: 'Note', id: '11111111-1111-4111-8111-111111111111', label: 'Buy paper' },
      dueAt,
      claimedBy: null,
      overdue: false,
      createdAt: '2026-10-10T09:30:00Z',
    },
    {
      id: approveId,
      releaseId,
      process: 'NoteApproval',
      step: 'approve',
      labelKey: 'noteApproval.task',
      subject: { entity: 'Note', id: '22222222-2222-4222-8222-222222222222', label: 'Order chairs' },
      dueAt: null,
      claimedBy: null,
      overdue: false,
      createdAt: '2026-10-09T09:30:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
})

const texts = {
  'shell.tasks.task': 'Task',
  'shell.tasks.subject': 'Record',
  'shell.tasks.dueAt': 'Due',
  'shell.tasks.overdue': 'Overdue',
  'shell.tasks.claimedBy': 'Claimed by {name}',
  'shell.tasks.empty': 'No open tasks.',
  'shell.tasks.loadFailed': 'The tasks could not be loaded.',
  'noteReview.task': 'Review the note',
  'noteApproval.task': 'Approve the note',
}
// The platform texts give the toolbar its English names, such as Search and Refresh.
const catalogs = [{ texts: { ...platformEnglish, ...texts }, fallbackTexts: { ...platformEnglish, ...texts } }]

/** The signed-in user, who is the claimer of the claimed rows. */
const reader = { id: 'maria', displayName: 'Maria Nguyen', roles: [], users: false }

/**
 * Stubs `fetch` with the task API, the signed-in user and the texts of the tasks' release, and returns the requested
 * URLs, without the user's. The release texts answer `releaseTexts` with `releaseStatus`.
 */
function stubTasks(
  body = taskPage,
  status = 200,
  { releaseTexts = {}, releaseStatus = 200 }: { releaseTexts?: Record<string, string>; releaseStatus?: number } = {},
) {
  const fetchMock = vi.fn(async (input: string) => {
    if (input === '/api/me') {
      return new Response(JSON.stringify(reader), { status: 200 })
    }
    return input.includes('/texts/')
      ? new Response(JSON.stringify({ locale: 'en', texts: releaseTexts }), { status: releaseStatus })
      : new Response(body, { status })
  })
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url).filter((url) => url !== '/api/me')
}

function CurrentSearch() {
  const location = useLocation()
  return (
    <>
      <output data-testid="search">{location.search}</output>
      <output data-testid="path">{location.pathname}</output>
      <output data-testid="from">{(location.state as { from?: string } | null)?.from}</output>
    </>
  )
}

function renderWidget(search = '', outerCatalogs: TextCatalog[] = catalogs, recordPages?: Record<string, string>) {
  return render(
    <MemoryRouter initialEntries={[`/e2e/tasks${search}`]}>
      <TextProvider catalogs={outerCatalogs} development={false}>
        <RecordPagesContext value={recordPages ? { sitePath: 'e2e', recordPages } : null}>
          <TaskInboxWidget sitePath="e2e" pageName="Tasks" widget={inboxWidget} locale="en" fallbackLocale="en" />
        </RecordPagesContext>
      </TextProvider>
      <CurrentSearch />
    </MemoryRouter>,
  )
}

describe('TaskInboxWidget', () => {
  it('tags only the overdue row as overdue and only the claimed row as claimed by the reader', async () => {
    const [review, approve] = (JSON.parse(taskPage) as { items: Record<string, unknown>[] }).items
    stubTasks(
      JSON.stringify({
        items: [
          { ...review, overdue: true, claimedBy: 'maria' },
          { ...approve, overdue: false, claimedBy: null },
        ],
        page: 1,
        pageSize: 20,
        totalCount: 2,
      }),
    )

    renderWidget()

    await screen.findByRole('row', { name: /Review the note/ })
    const [first, second] = screen.getAllByRole('row').slice(1)
    expect(within(first).getByText('Overdue')).toBeInTheDocument()
    expect(within(first).getByText('Overdue').closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
    await waitFor(() =>
      expect(first.querySelector('[data-tone="neutral"]')).toHaveTextContent(/^Claimed by Maria Nguyen \(you\)$/),
    )
    expect(within(second).queryByText('Overdue')).not.toBeInTheDocument()
    expect(second.querySelector('[data-tone="neutral"]')).toBeNull()
  })

  it('lists the tasks in the order of the task API, each linking to its address under the inbox page', async () => {
    stubTasks()

    renderWidget()

    await screen.findByRole('row', { name: /Review the note/ })
    for (const title of ['Task', 'Record', 'Due']) {
      expect(screen.getByRole('columnheader', { name: title })).toBeInTheDocument()
    }
    const [review, approve] = screen.getAllByRole('row').slice(1)
    expect(within(review).getByRole('link', { name: 'Review the note' })).toHaveAttribute(
      'href',
      `/e2e/tasks/${reviewId}`,
    )
    expect(within(review).getByText('Buy paper')).toBeInTheDocument()
    const due = formatValue({ name: 'dueAt', type: 'date-time' }, dueAt, {}, { locale: 'en', text: (key) => key })
    expect(within(review).getByText(due)).toBeInTheDocument()

    expect(within(approve).getByRole('link', { name: 'Approve the note' })).toHaveAttribute(
      'href',
      `/e2e/tasks/${approveId}`,
    )
    expect(within(approve).getByText('Order chairs')).toBeInTheDocument()
    // No due date shows the em dash.
    expect(within(approve).getAllByRole('cell')[2]).toHaveTextContent('—')
  })

  it("links the Record column to the subject's record page, passing the inbox's address as from", async () => {
    stubTasks()

    renderWidget('?pageSize=10', catalogs, { Note: '/e2e/noteform' })

    const link = await screen.findByRole('link', { name: 'Buy paper' })
    expect(link).toHaveAttribute('href', '/e2e/noteform/11111111-1111-4111-8111-111111111111')
    await userEvent.click(link)
    expect(screen.getByTestId('path')).toHaveTextContent('/e2e/noteform/11111111-1111-4111-8111-111111111111')
    expect(screen.getByTestId('from')).toHaveTextContent('/e2e/tasks?pageSize=10')
  })

  it('shows the Record column as plain text when the subject entity has no record page', async () => {
    stubTasks()

    renderWidget('', catalogs, { Request: '/e2e/requestform' })

    expect(await screen.findByText('Buy paper')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Buy paper' })).not.toBeInTheDocument()
  })

  it('shows the muted dash, not a link or the id, when the subject has no label', async () => {
    const [review, approve] = (JSON.parse(taskPage) as { items: Record<string, unknown>[] }).items
    stubTasks(
      JSON.stringify({
        items: [
          { ...review, subject: { entity: 'Note', id: '11111111-1111-4111-8111-111111111111', label: null } },
          approve,
        ],
        page: 1,
        pageSize: 20,
        totalCount: 2,
      }),
    )

    renderWidget('', catalogs, { Note: '/e2e/noteform' })

    await screen.findByRole('row', { name: /Review the note/ })
    const [first, second] = screen.getAllByRole('row').slice(1)
    expect(within(first).getAllByRole('cell')[1]).toHaveTextContent('—')
    expect(within(first).queryByText('11111111-1111-4111-8111-111111111111')).not.toBeInTheDocument()
    expect(within(first).queryByRole('link', { name: /1111/ })).not.toBeInTheDocument()
    expect(within(second).getByRole('link', { name: 'Order chairs' })).toBeInTheDocument()
  })

  it("renders a step label from the texts of the task's release when the site texts lack the key", async () => {
    const requests = stubTasks(taskPage, 200, { releaseTexts: { 'noteReview.task': 'Check the note' } })
    const { 'noteReview.task': _removed, ...siteTexts } = texts

    renderWidget('', [{ texts: siteTexts, fallbackTexts: siteTexts }])

    expect(await screen.findByRole('link', { name: 'Check the note' })).toHaveAttribute(
      'href',
      `/e2e/tasks/${reviewId}`,
    )
    // Both tasks are on one release, so its texts are fetched once.
    expect(requests().filter((url) => url.includes('/texts/'))).toEqual([
      `/api/sites/e2e/texts/en?release=${releaseId}`,
    ])
  })

  it("shows the load error when the texts of the tasks' releases fail to load", async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubTasks(taskPage, 200, { releaseStatus: 500 })

    renderWidget()

    expect(await screen.findByTestId('tasks-error')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('asks to sign in when nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    history.pushState(null, '', '/e2e/tasks?pageSize=100')
    stubTasks('', 401)

    renderWidget('?pageSize=100')

    await waitFor(() => expect(isSignInNeeded()).toBe(true))
    expect(assignLocation).not.toHaveBeenCalled()
    expect(screen.queryByTestId('tasks-sign-in')).not.toBeInTheDocument()
  })

  it('shows an error when the tasks fail to load, with no rows and the toolbar still shown', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubTasks('', 500)

    renderWidget()

    expect(await screen.findByTestId('tasks-error')).toHaveTextContent('The tasks could not be loaded.')
    expect(screen.queryByRole('row', { name: /Review the note/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument()
  })

  it('searches, filters on Record, sorts by Due and keeps the view in the address', async () => {
    const requests = stubTasks()
    const user = userEvent.setup()
    const filter = JSON.stringify({ all: [{ field: 'subject', op: 'contains', value: 'paper' }] })

    renderWidget(`?filter=${encodeURIComponent(filter)}`)

    await screen.findByRole('row', { name: /Review the note/ })
    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'review{Enter}')
    await user.click(screen.getByRole('columnheader', { name: 'Due' }))

    const tasksRequests = () => requests().filter((url) => url.startsWith(tasksPath))
    await waitFor(() =>
      expect(tasksRequests().at(-1)).toBe(
        `${tasksPath}?${new URLSearchParams({ search: 'review', filter, sort: 'dueAt' })}`,
      ),
    )
    const address = new URLSearchParams(screen.getByTestId('search').textContent ?? '')
    expect(address.get('search')).toBe('review')
    expect(JSON.parse(address.get('filter')!)).toEqual({ all: [{ field: 'subject', op: 'contains', value: 'paper' }] })
    expect(address.get('sort')).toBe('dueAt')
    expect(await screen.findByRole('row', { name: /Review the note/ })).toBeInTheDocument()
  })

  it('hides the Record column through Columns and keeps the choice in the address', async () => {
    stubTasks()
    const user = userEvent.setup()

    renderWidget()

    await screen.findByRole('row', { name: /Review the note/ })
    await user.click(screen.getByRole('button', { name: 'Columns' }))
    const dialog = screen.getByRole('dialog')
    await user.click(within(dialog).getByRole('checkbox', { name: 'Record' }))
    await user.click(within(dialog).getByRole('button', { name: 'Apply' }))

    expect(screen.queryByRole('columnheader', { name: 'Record' })).not.toBeInTheDocument()
    const address = new URLSearchParams(screen.getByTestId('search').textContent ?? '')
    expect(address.get('columns')).toBe('task,dueAt')
  })

  it('loads the same tasks again on Refresh', async () => {
    const requests = stubTasks()
    const user = userEvent.setup()

    renderWidget()

    await screen.findByRole('row', { name: /Review the note/ })
    await user.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(requests().filter((url) => url.startsWith(tasksPath))).toEqual([tasksPath, tasksPath]))
    expect(await screen.findByRole('row', { name: /Review the note/ })).toBeInTheDocument()
  })

  it('requests the page and page size in the URL', async () => {
    const requests = stubTasks()

    renderWidget('?page=2&pageSize=10')

    await screen.findByRole('row', { name: /Review the note/ })
    expect(requests()).toContain(`${tasksPath}?page=2&pageSize=10`)
  })

  it('drops invalid parameters and requests the default page', async () => {
    const requests = stubTasks()

    renderWidget('?page=0&pageSize=7')

    await screen.findByRole('row', { name: /Review the note/ })
    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe(''))
    expect(requests().filter((url) => url.startsWith(tasksPath))).toEqual([tasksPath])
  })
})
