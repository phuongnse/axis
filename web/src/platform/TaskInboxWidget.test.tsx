import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import { formatValue } from './formatValue'
import { assignLocation } from './navigation'
import type { WidgetMetadata } from './site'
import { TaskInboxWidget } from './TaskInboxWidget'
import { TextProvider } from './TextProvider'
import type { TextCatalog } from './texts'

vi.mock('./navigation')

const tasksPath = '/api/apps/E2eApp/tasks'

const inboxWidget: WidgetMetadata = {
  type: 'taskInbox',
  formPage: null,
  entity: null,
  dataSource: null,
  form: null,
  tasksPath,
  process: null,
  instancesPath: null,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  access: null,
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
  'shell.tasks.empty': 'No open tasks.',
  'shell.tasks.loadFailed': 'The tasks could not be loaded.',
  'noteReview.task': 'Review the note',
  'noteApproval.task': 'Approve the note',
}
const catalogs = [{ texts, fallbackTexts: texts }]

/**
 * Stubs `fetch` with the task API and the texts of the tasks' release, and returns the requested
 * URLs. The release texts answer `releaseTexts` with `releaseStatus`.
 */
function stubTasks(
  body = taskPage,
  status = 200,
  { releaseTexts = {}, releaseStatus = 200 }: { releaseTexts?: Record<string, string>; releaseStatus?: number } = {},
) {
  const fetchMock = vi.fn(async (input: string) =>
    input.includes('/texts/')
      ? new Response(JSON.stringify({ locale: 'en', texts: releaseTexts }), { status: releaseStatus })
      : new Response(body, { status }),
  )
  vi.stubGlobal('fetch', fetchMock)
  return () => fetchMock.mock.calls.map(([url]) => url)
}

function CurrentSearch() {
  return <output data-testid="search">{useLocation().search}</output>
}

function renderWidget(search = '', outerCatalogs: TextCatalog[] = catalogs) {
  return render(
    <MemoryRouter initialEntries={[`/e2e/tasks${search}`]}>
      <TextProvider catalogs={outerCatalogs} development={false}>
        <TaskInboxWidget sitePath="e2e" pageName="Tasks" widget={inboxWidget} locale="en" fallbackLocale="en" />
      </TextProvider>
      <CurrentSearch />
    </MemoryRouter>,
  )
}

describe('TaskInboxWidget', () => {
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
    // No due date is an empty cell.
    expect(within(approve).getAllByRole('cell')[2]).toHaveTextContent(/^$/)
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

  it('starts sign-in with the current path and query string when nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    history.pushState(null, '', '/e2e/tasks?pageSize=100')
    stubTasks('', 401)

    renderWidget('?pageSize=100')

    await waitFor(() =>
      expect(assignLocation).toHaveBeenCalledExactlyOnceWith(
        '/api/auth/sign-in?returnUrl=%2Fe2e%2Ftasks%3FpageSize%3D100',
      ),
    )
    expect(screen.queryByTestId('tasks-sign-in')).not.toBeInTheDocument()
  })

  it('shows an error when the tasks fail to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubTasks('', 500)

    renderWidget()

    expect(await screen.findByText('The tasks could not be loaded.')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('requests the page and page size in the URL', async () => {
    const requests = stubTasks()

    renderWidget('?page=2&pageSize=10')

    await screen.findByRole('row', { name: /Review the note/ })
    expect(requests()).toContain(`${tasksPath}?page=2&pageSize=10`)
  })

  it('drops invalid parameters and a sort, and requests the default page', async () => {
    const requests = stubTasks()

    renderWidget('?page=0&pageSize=7&sort=dueAt')

    await screen.findByRole('row', { name: /Review the note/ })
    await waitFor(() => expect(screen.getByTestId('search').textContent).toBe(''))
    expect(requests().filter((url) => url.startsWith(tasksPath))).toEqual([tasksPath])
  })
})
