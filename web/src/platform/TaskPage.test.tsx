import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { formatValue } from './formatValue'
import { assignLocation } from './navigation'
import type { FieldMetadata, FieldType, WidgetMetadata } from './site'
import { TaskPage } from './TaskPage'
import { TextProvider } from './TextProvider'
import type { TextCatalog } from './texts'

vi.mock('./navigation')

function field(name: string, type: FieldType, labelKey: string | null): FieldMetadata {
  return {
    name,
    type,
    labelKey,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    values: null,
    target: null,
    fields: null,
  }
}

const tasksPath = '/api/apps/PurchasingApp/tasks'
const recordsPath = '/api/apps/PurchasingApp/entities/PurchaseRequest/records'
const taskId = '0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01'
const subjectId = '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b'
const releaseId = '0192f4a0-1b2c-7d3e-8f40-5b6c7d8e9f02'
const dueAt = '2026-10-13T02:05:00Z'

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
  kpi: null,
}

// The task read example of the processes reference, with a read-only title and an editable comment.
const openTask = {
  id: taskId,
  releaseId,
  process: 'PurchaseRequestApproval',
  instanceId: '0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b',
  step: 'managerApproval',
  labelKey: 'purchaseRequest.managerApproval',
  state: 'open',
  assignee: { user: 'maria', role: null },
  subject: { entity: 'PurchaseRequest', id: subjectId, label: 'PR-2026-00042' },
  form: {
    name: 'PurchaseRequestReview',
    sections: [
      {
        titleKey: 'purchaseRequest.sections.decision',
        fields: [
          { name: 'title', readOnly: true },
          { name: 'decisionComments', readOnly: false },
        ],
      },
    ],
    entity: {
      name: 'PurchaseRequest',
      labelKey: null,
      displayField: 'title',
      recordsPath,
      fields: [
        { ...field('title', 'text', 'purchaseRequest.title'), required: true, maxLength: 200 },
        { ...field('decisionComments', 'text', 'purchaseRequest.decisionComments'), maxLength: 2000 },
      ],
    },
  },
  outcomes: [
    { name: 'approve', labelKey: 'task.approve' },
    { name: 'return', labelKey: 'task.return' },
    { name: 'reject', labelKey: 'task.reject' },
  ],
  dueAt,
  createdAt: '2026-10-10T02:05:00.123456Z',
  completedAt: null,
  completedBy: null,
  outcome: null,
}

const completedTask = {
  ...openTask,
  state: 'completed',
  completedAt: '2026-10-11T02:05:00Z',
  completedBy: 'maria',
  outcome: 'approve',
}

const subject = JSON.stringify({
  id: subjectId,
  version: 3,
  values: { title: 'Laptops for Q4', decisionComments: null },
  labels: {},
})

const texts = {
  ...platformEnglish,
  'purchaseRequest.managerApproval': 'Manager approval',
  'purchaseRequest.sections.decision': 'Decision',
  'purchaseRequest.title': 'Title',
  'purchaseRequest.decisionComments': 'Comments',
  'task.approve': 'Approve',
  'task.return': 'Return',
  'task.reject': 'Reject',
}
const catalogs = [{ texts, fallbackTexts: texts }]

interface Answer {
  status: number
  body?: unknown
}

function answer({ status, body }: Answer) {
  return new Response(body === undefined ? '' : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

/**
 * Stubs `fetch` with the task read, the subject record read, the complete endpoint and the texts of
 * the task's release. Each read of the task takes the next answer of `reads`, and the last one
 * repeats. Returns the requests.
 */
function stubTaskApi({
  reads = [{ status: 200, body: openTask }],
  complete = { status: 200, body: completedTask },
  releaseTexts = { status: 200, body: { locale: 'en', texts: {} } },
}: { reads?: Answer[]; complete?: Answer; releaseTexts?: Answer } = {}) {
  const requests: { method: string; url: string; body?: string }[] = []
  let taskReads = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      requests.push({ method, url, body: init?.body as string | undefined })
      if (method === 'GET' && url === `${tasksPath}/${taskId}`) {
        return answer(reads[Math.min(taskReads++, reads.length - 1)])
      }
      if (method === 'GET' && url === `${recordsPath}/${subjectId}`) {
        return new Response(subject, { status: 200 })
      }
      if (method === 'POST' && url === `${tasksPath}/${taskId}/complete`) {
        return answer(complete)
      }
      if (method === 'GET' && url === `/api/sites/e2e/texts/en?release=${releaseId}`) {
        return answer(releaseTexts)
      }
      return answer({ status: 404 })
    }),
  )
  return {
    taskReads: () => requests.filter(({ method, url }) => method === 'GET' && url === `${tasksPath}/${taskId}`),
    completions: () => requests.filter(({ method }) => method === 'POST'),
  }
}

function CurrentPath() {
  return <output data-testid="location">{useLocation().pathname}</output>
}

function renderPage(outerCatalogs: TextCatalog[] = catalogs) {
  return render(
    <MemoryRouter initialEntries={[`/e2e/tasks/${taskId}`]}>
      <TextProvider catalogs={outerCatalogs} development={false}>
        <TaskPage
          sitePath="e2e"
          pageName="Tasks"
          widget={inboxWidget}
          taskId={taskId}
          locale="en"
          fallbackLocale="en"
        />
      </TextProvider>
      <CurrentPath />
    </MemoryRouter>,
  )
}

describe('TaskPage', () => {
  it('shows the step label, the subject, the due date and the form with one button per outcome', async () => {
    stubTaskApi()

    renderPage()

    expect(await screen.findByLabelText('Title')).toHaveValue('Laptops for Q4')
    expect(screen.getByText('Manager approval')).toBeInTheDocument()
    expect(screen.getByText('PR-2026-00042')).toBeInTheDocument()
    const due = formatValue({ name: 'dueAt', type: 'date-time' }, dueAt, {}, { locale: 'en', text: (key) => key })
    expect(screen.getByText(due)).toBeInTheDocument()
    expect(screen.getByText('Decision')).toBeInTheDocument()
    expect(screen.getByLabelText('Title')).toHaveAttribute('readonly')
    expect(screen.getByLabelText('Comments')).not.toHaveAttribute('readonly')
    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Approve', 'Return', 'Reject'])
  })

  it("renders a label from the texts of the task's release when the site texts lack the key", async () => {
    stubTaskApi({
      releaseTexts: {
        status: 200,
        body: { locale: 'en', texts: { 'purchaseRequest.managerApproval': 'Manager review' } },
      },
    })
    const { 'purchaseRequest.managerApproval': _removed, ...siteTexts } = texts

    renderPage([{ texts: siteTexts, fallbackTexts: siteTexts }])

    expect(await screen.findByLabelText('Title')).toHaveValue('Laptops for Q4')
    expect(screen.getByText('Manager review')).toBeInTheDocument()
  })

  it("shows the load error when the texts of the task's release fail to load", async () => {
    stubTaskApi({ releaseTexts: { status: 500 } })

    renderPage()

    expect(await screen.findByTestId('task-load-error')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('completes the task with the outcome, only the changed editable fields and the version, then goes to the inbox', async () => {
    const api = stubTaskApi()
    renderPage()

    await userEvent.type(await screen.findByLabelText('Comments'), 'Fine')
    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/e2e/tasks'))
    expect(api.completions().map(({ body }) => body)).toEqual([
      '{"outcome":"approve","version":3,"values":{"decisionComments":"Fine"}}',
    ])
  })

  it('sends the version with no values when nothing changed', async () => {
    const api = stubTaskApi()
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Reject' }))

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/e2e/tasks'))
    expect(api.completions().map(({ body }) => body)).toEqual(['{"outcome":"reject","version":3,"values":{}}'])
  })

  it('shows a 400 under the field it names and other keys above the form', async () => {
    stubTaskApi({
      complete: {
        status: 400,
        body: {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: {
            '/values/decisionComments': ['Too long.'],
            '/outcome': ["Must be one of the step's outcomes."],
          },
        },
      },
    })
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByTestId('field-error-decisionComments')).toHaveTextContent('Too long.')
    expect(screen.getByTestId('form-error')).toHaveTextContent("Must be one of the step's outcomes.")
    expect(screen.getByTestId('location').textContent).toBe(`/e2e/tasks/${taskId}`)
  })

  it('shows that the task is assigned to someone else when the read answers 403', async () => {
    stubTaskApi({ reads: [{ status: 403, body: { title: 'This task is assigned to someone else.', status: 403 } }] })

    renderPage()

    expect(await screen.findByText('This task is assigned to someone else.')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('shows that the task is assigned to someone else when completing answers 403', async () => {
    stubTaskApi({ complete: { status: 403, body: { title: 'This task is assigned to someone else.', status: 403 } } })
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByText('This task is assigned to someone else.')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows that the task is already completed on a 409 with no keys, loads it again and removes the buttons', async () => {
    const api = stubTaskApi({
      reads: [
        { status: 200, body: openTask },
        { status: 200, body: completedTask },
      ],
      complete: { status: 409, body: { title: 'This task is already completed.', status: 409 } },
    })
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByText('This task is already completed.')).toBeInTheDocument()
    await waitFor(() => expect(api.taskReads()).toHaveLength(2))
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('shows a 409 that names a field as the form widget does', async () => {
    stubTaskApi({
      complete: {
        status: 409,
        body: {
          title: 'The record has changed since this version was read.',
          status: 409,
          errors: { '/version': ["Must be the record's current version."] },
        },
      },
    })
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByTestId('form-error')).toHaveTextContent("Must be the record's current version.")
    // The loading icon fades out after the answer, so the button is found by its text.
    expect(screen.getByText('Approve').closest('button')).toBeEnabled()
  })

  it('shows only the completed message for a task that is not open', async () => {
    const api = stubTaskApi({ reads: [{ status: 200, body: completedTask }] })

    renderPage()

    expect(await screen.findByText('This task is already completed.')).toBeInTheDocument()
    expect(screen.getByText('Manager approval')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(api.completions()).toHaveLength(0)
  })

  it('shows the not-found page when the task API has no task with the id', async () => {
    stubTaskApi({ reads: [{ status: 404, body: { title: 'No task exists with this id.', status: 404 } }] })

    renderPage()

    expect(await screen.findByTestId('not-found')).toBeInTheDocument()
  })

  it('starts sign-in with the current path and query string when nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    history.pushState(null, '', `/e2e/tasks/${taskId}?from=inbox`)
    stubTaskApi({ reads: [{ status: 401, body: { title: 'Sign in to read or write this data.', status: 401 } }] })

    renderPage()

    await waitFor(() =>
      expect(assignLocation).toHaveBeenCalledExactlyOnceWith(
        `/api/auth/sign-in?returnUrl=%2Fe2e%2Ftasks%2F${taskId}%3Ffrom%3Dinbox`,
      ),
    )
    expect(screen.queryByTestId('tasks-sign-in')).not.toBeInTheDocument()
  })

  it('shows an error when the task fails to load, and when completing fails', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubTaskApi({ reads: [{ status: 500 }] })
    const { unmount } = renderPage()

    expect(await screen.findByText('The task could not be loaded.')).toBeInTheDocument()
    unmount()

    stubTaskApi({ complete: { status: 500 } })
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

    expect(await screen.findByTestId('form-error')).toHaveTextContent('The decision could not be saved.')
  })
})
