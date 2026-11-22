import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { formatValue } from './formatValue'
import { isSignInNeeded } from './signInNeeded'
import { assignLocation } from './navigation'
import { RecordPagesContext } from './recordPages'
import type { FieldMetadata, FieldType, WidgetMetadata } from './site'
import { TaskPage } from './TaskPage'
import { followLink, renderGuarded } from '../test/leaveGuard'
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
    min: null,
    max: null,
    minLength: null,
    values: null,
    target: null,
    fields: null,
  }
}

const tasksPath = '/api/apps/PurchasingApp/tasks'
const usersUrl = '/api/apps/PurchasingApp/users'
const recordsPath = '/api/apps/PurchasingApp/entities/PurchaseRequest/records'
const taskId = '0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01'
const subjectId = '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b'
const releaseId = '0192f4a0-1b2c-7d3e-8f40-5b6c7d8e9f02'
const dueAt = '2026-10-13T02:05:00Z'

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
    steps: [],
    sections: [
      {
        name: 'decision',
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
  claimedBy: null,
  overdue: false,
  commands: [] as string[],
  reassignRoles: [],
  createdAt: '2026-10-10T02:05:00.123456Z',
  completedAt: null,
  completedBy: null,
  outcome: null,
}

// The same task with a form of two steps: the editable comments, then the read-only title.
const stepsTask = {
  ...openTask,
  form: {
    ...openTask.form,
    sections: [
      {
        name: 'comments',
        titleKey: 'purchaseRequest.sections.comments',
        fields: [{ name: 'decisionComments', readOnly: false }],
      },
      { name: 'summary', titleKey: 'purchaseRequest.sections.summary', fields: [{ name: 'title', readOnly: true }] },
    ],
    steps: [
      { titleKey: 'purchaseRequest.steps.comments', sections: ['comments'] },
      { titleKey: 'purchaseRequest.steps.summary', sections: ['summary'] },
    ],
  },
}

const completedTask = {
  ...openTask,
  state: 'completed',
  completedAt: '2026-10-11T02:05:00Z',
  completedBy: 'maria',
  outcome: 'approve',
}

const expiredTask = { ...openTask, state: 'expired' }

const subject = JSON.stringify({
  id: subjectId,
  version: 3,
  values: { title: 'Laptops for Q4', decisionComments: null },
  labels: {},
})

const texts = {
  ...platformEnglish,
  'page.tasks': 'Tasks',
  'purchaseRequest.managerApproval': 'Manager approval',
  'purchaseRequest.sections.decision': 'Decision',
  'purchaseRequest.title': 'Title',
  'purchaseRequest.decisionComments': 'Comments',
  'purchaseRequest.sections.comments': 'Remarks',
  'purchaseRequest.sections.summary': 'Summary',
  'purchaseRequest.steps.comments': 'Step one',
  'purchaseRequest.steps.summary': 'Step two',
  'task.approve': 'Approve',
  'task.return': 'Return',
  'task.reject': 'Reject',
  'role.finance': 'Finance team',
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
 * Stubs `fetch` with the task read, the subject record read, the complete and save endpoints and the texts of
 * the task's release. Each read of the task takes the next answer of `reads`, and each read of the subject record the
 * next body of `subjects`, and the last one repeats. Each form state call takes the next answer of `states`, and the last one repeats, with
 * every field shown when `states` is empty. Returns the requests.
 */
function stubTaskApi({
  reads = [{ status: 200, body: openTask }],
  complete = { status: 200, body: completedTask },
  save = { status: 200, body: { version: 4 } },
  subjects = [subject],
  command = { status: 200, body: { ...openTask, commands: ['release'] } },
  reassign = { status: 200, body: { ...openTask, assignee: { user: null, role: 'procurement' } } },
  users = { status: 200, body: { items: [] } },
  releaseTexts = { status: 200, body: { locale: 'en', texts: {} } },
  states = [{ sections: [], fields: {}, errors: {} }],
}: {
  reads?: Answer[]
  complete?: Answer
  save?: Answer
  subjects?: string[]
  command?: Answer
  reassign?: Answer
  users?: Answer | ((search: string) => Answer)
  releaseTexts?: Answer
  states?: unknown[]
} = {}) {
  const requests: { method: string; url: string; body?: string }[] = []
  let taskReads = 0
  let subjectReads = 0
  let stateCalls = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      requests.push({ method, url, body: init?.body as string | undefined })
      if (method === 'GET' && url === `${tasksPath}/${taskId}`) {
        return answer(reads[Math.min(taskReads++, reads.length - 1)])
      }
      if (method === 'GET' && url === `${recordsPath}/${subjectId}`) {
        return new Response(subjects[Math.min(subjectReads++, subjects.length - 1)], { status: 200 })
      }
      if (method === 'POST' && url === `${tasksPath}/${taskId}/complete`) {
        return answer(complete)
      }
      if (method === 'POST' && url === `${tasksPath}/${taskId}/save`) {
        return answer(save)
      }
      if (method === 'POST' && (url === `${tasksPath}/${taskId}/claim` || url === `${tasksPath}/${taskId}/release`)) {
        return answer(command)
      }
      if (method === 'POST' && url === `${tasksPath}/${taskId}/reassign`) {
        return answer(reassign)
      }
      if (method === 'GET' && url.startsWith(`${usersUrl}?search=`)) {
        const search = decodeURIComponent(url.slice(`${usersUrl}?search=`.length))
        return answer(typeof users === 'function' ? users(search) : users)
      }
      if (method === 'POST' && url === `${tasksPath}/${taskId}/form-state`) {
        return answer({ status: 200, body: states[Math.min(stateCalls++, states.length - 1)] })
      }
      if (method === 'GET' && url === `/api/sites/e2e/texts/en?release=${releaseId}`) {
        return answer(releaseTexts)
      }
      return answer({ status: 404 })
    }),
  )
  return {
    taskReads: () => requests.filter(({ method, url }) => method === 'GET' && url === `${tasksPath}/${taskId}`),
    commands: () =>
      requests.filter(({ method, url }) => method === 'POST' && /\/(claim|release)$/.test(url)).map(({ url }) => url),
    subjectReads: () => requests.filter(({ method, url }) => method === 'GET' && url === `${recordsPath}/${subjectId}`),
    reassigns: () =>
      requests
        .filter(({ method, url }) => method === 'POST' && url.endsWith('/reassign'))
        .map(({ body }) => JSON.parse(body ?? '{}') as Record<string, unknown>),
    userSearches: () =>
      requests
        .filter(({ method, url }) => method === 'GET' && url.startsWith(`${usersUrl}?search=`))
        .map(({ url }) => decodeURIComponent(url.slice(`${usersUrl}?search=`.length))),
    saves: () =>
      requests.filter(({ method, url }) => method === 'POST' && url.endsWith('/save')).map(({ body }) => body),
    completions: () => requests.filter(({ method, url }) => method === 'POST' && url.endsWith('/complete')),
    stateRequests: () =>
      requests
        .filter(({ method, url }) => method === 'POST' && url.endsWith('/form-state'))
        .map(({ url, body }) => ({ url, body: JSON.parse(body ?? '{}') as Record<string, unknown> })),
  }
}

function CurrentPath() {
  return <output data-testid="location">{useLocation().pathname}</output>
}

function taskPage(outerCatalogs: TextCatalog[] = catalogs, recordPages: Record<string, string> | null = null) {
  return (
    <TextProvider catalogs={outerCatalogs} development={false}>
      <RecordPagesContext value={recordPages ? { sitePath: 'e2e', recordPages } : null}>
        <TaskPage
          sitePath="e2e"
          pageName="Tasks"
          titleKey="page.tasks"
          widget={inboxWidget}
          taskId={taskId}
          locale="en"
          fallbackLocale="en"
        />
      </RecordPagesContext>
    </TextProvider>
  )
}

function renderPage(outerCatalogs: TextCatalog[] = catalogs, recordPages: Record<string, string> | null = null) {
  return render(
    <MemoryRouter initialEntries={[`/e2e/tasks/${taskId}`]}>
      {taskPage(outerCatalogs, recordPages)}
      <CurrentPath />
    </MemoryRouter>,
  )
}

// Renders the page under the guard of unsaved changes. Returns the router, so a test reads the address.
function renderGuardedPage() {
  return renderGuarded(taskPage())
}

describe('TaskPage', () => {
  it('shows the step label, the subject, the due date and the form with one button per outcome', async () => {
    stubTaskApi()

    renderPage()

    expect(await screen.findByTestId('field-value-title')).toHaveTextContent('Laptops for Q4')
    expect(screen.getByText('Manager approval')).toBeInTheDocument()
    expect(screen.getByText('PR-2026-00042')).toBeInTheDocument()
    const due = formatValue({ name: 'dueAt', type: 'date-time' }, dueAt, {}, { locale: 'en', text: (key) => key })
    expect(screen.getByText(due)).toBeInTheDocument()
    expect(screen.getByText('Decision')).toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()
    expect(screen.getByLabelText('Comments')).not.toHaveAttribute('readonly')
    expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Approve', 'Return', 'Reject'])
  })

  it('is headed by the step label, with Task as its line, and has no title of the inbox', async () => {
    stubTaskApi()

    renderPage()

    await screen.findByTestId('field-value-title')
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Manager approval')
    expect(within(screen.getByTestId('page-heading')).getByText('Task')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { level: 1, name: 'Tasks' })).not.toBeInTheDocument()
  })

  it('lists Record and Due as the first part, and Record is a link where the entity has a record page', async () => {
    stubTaskApi()
    const { unmount } = renderPage(catalogs, { PurchaseRequest: '/e2e/requests' })

    const facts = await screen.findByTestId('task-facts')
    expect(
      within(facts)
        .getAllByRole('term')
        .map((term) => term.textContent),
    ).toEqual(['Record', 'Due'])
    expect(within(facts).getByRole('link', { name: 'PR-2026-00042' })).toHaveAttribute(
      'href',
      `/e2e/requests/${subjectId}`,
    )
    unmount()

    stubTaskApi()
    renderPage()
    const plain = await screen.findByTestId('task-facts')
    expect(within(plain).getByText('PR-2026-00042')).toBeInTheDocument()
    expect(within(plain).queryByRole('link')).not.toBeInTheDocument()
  })

  it('shows no Due fact for a task with no due date', async () => {
    stubTaskApi({ reads: [{ status: 200, body: { ...openTask, dueAt: null } }] })

    renderPage()

    const facts = await screen.findByTestId('task-facts')
    expect(
      within(facts)
        .getAllByRole('term')
        .map((term) => term.textContent),
    ).toEqual(['Record'])
  })

  it('puts the outcomes on the heading as one group named Decision, after the commands', async () => {
    stubTaskApi({ reads: [{ status: 200, body: { ...openTask, commands: ['claim', 'release', 'reassign'] } }] })

    renderPage()

    await screen.findByTestId('field-value-title')
    const actions = screen.getByTestId('page-actions')
    expect(
      within(actions)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Release', 'Claim', 'Reassign…', 'Approve', 'Return', 'Reject'])
    const group = within(actions).getByRole('group', { name: 'Decision' })
    expect(
      within(group)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Approve', 'Return', 'Reject'])
    expect(within(screen.getByTestId('task-form')).queryByRole('button')).not.toBeInTheDocument()
  })

  it('shows one outcome as a plain button', async () => {
    stubTaskApi({
      reads: [{ status: 200, body: { ...openTask, outcomes: [{ name: 'approve', labelKey: 'task.approve' }] } }],
    })

    renderPage()

    await screen.findByTestId('field-value-title')
    expect(within(screen.getByTestId('page-actions')).getByRole('button', { name: 'Approve' })).toBeInTheDocument()
    expect(screen.queryByRole('group', { name: 'Decision' })).not.toBeInTheDocument()
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

    expect(await screen.findByTestId('field-value-title')).toHaveTextContent('Laptops for Q4')
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

  it("asks the task's form state endpoint with the values and touched names and no record id", async () => {
    const api = stubTaskApi()
    renderPage()

    await screen.findByTestId('field-value-title')
    await waitFor(() => expect(api.stateRequests()).toHaveLength(1))
    const [first] = api.stateRequests()
    expect(first.url).toBe(`${tasksPath}/${taskId}/form-state`)
    expect(first.body).toEqual({ values: {}, touched: [] })
    expect(first.body).not.toHaveProperty('recordId')

    await userEvent.type(screen.getByLabelText('Comments'), 'Fine')

    await waitFor(() => {
      const newest = api.stateRequests().at(-1)
      expect(newest?.body).toEqual({ values: { decisionComments: 'Fine' }, touched: ['decisionComments'] })
    })
  })

  it('does not show a field the form state hides and leaves it out of the completion', async () => {
    const shown = { sections: [], fields: {}, errors: {} }
    const hidden = {
      sections: [],
      fields: { decisionComments: { visible: false, required: false, readOnly: false } },
      errors: {},
    }
    const api = stubTaskApi({ states: [shown, hidden] })
    renderPage()

    await userEvent.type(await screen.findByLabelText('Comments'), 'Fine')

    await waitFor(() => expect(screen.queryByLabelText('Comments')).not.toBeInTheDocument())
    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))

    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/e2e/tasks'))
    expect(api.completions().map(({ body }) => body)).toEqual(['{"outcome":"approve","version":3,"values":{}}'])
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

  it('scrolls a finish error above the form into view and focuses it', async () => {
    const scrollIntoView = vi.fn()
    Element.prototype.scrollIntoView = scrollIntoView
    try {
      stubTaskApi({
        complete: {
          status: 400,
          body: {
            title: 'One or more validation errors occurred.',
            status: 400,
            errors: { '/outcome': ["Must be one of the step's outcomes."] },
          },
        },
      })
      renderPage()

      await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

      const error = await screen.findByTestId('form-error')
      await waitFor(() => expect(scrollIntoView).toHaveBeenCalledWith({ block: 'center' }))
      expect(scrollIntoView.mock.contexts).toEqual([error])
      expect(error).toHaveFocus()
      expect(within(error).getByRole('alert')).toHaveTextContent("Must be one of the step's outcomes.")
    } finally {
      delete (Element.prototype as Partial<Element>).scrollIntoView
    }
  })

  it('shows that the task is assigned to someone else when the read answers 403', async () => {
    stubTaskApi({ reads: [{ status: 403, body: { title: 'This task is assigned to someone else.', status: 403 } }] })

    renderPage()

    expect(await screen.findByText('This task is assigned to someone else.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Tasks')
    expect(screen.queryByTestId('record-not-found')).not.toBeInTheDocument()
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

    expect(await screen.findByText('This task is already done.')).toBeInTheDocument()
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

    expect(await screen.findByText('This task is already done.')).toBeInTheDocument()
    expect(screen.getByText('Manager approval')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(api.completions()).toHaveLength(0)
  })

  it("shows the overdue tag beside the step's label of an overdue task only", async () => {
    stubTaskApi({ reads: [{ status: 200, body: { ...openTask, overdue: true } }] })
    const { unmount } = renderPage()

    const tag = await within(await screen.findByTestId('page-heading')).findByText('Overdue')
    expect(tag.closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
    expect(within(screen.getByTestId('task-facts')).queryByText('Overdue')).not.toBeInTheDocument()
    unmount()

    stubTaskApi()
    renderPage()
    await screen.findByTestId('field-value-title')
    expect(screen.queryByText('Overdue')).not.toBeInTheDocument()
  })

  it('shows Claim, Release and Reassign exactly as the commands list them', async () => {
    for (const [commands, buttons] of [
      [[], []],
      [['claim'], ['Claim']],
      [['release'], ['Release']],
      [['reassign'], ['Reassign…']],
      [
        ['claim', 'release', 'reassign'],
        ['Claim', 'Release', 'Reassign…'],
      ],
    ] as const) {
      stubTaskApi({ reads: [{ status: 200, body: { ...openTask, commands } }] })
      const { unmount } = renderPage()

      await screen.findByTestId('field-value-title')
      expect(screen.queryByRole('button', { name: 'Claim' }) !== null).toBe(buttons.includes('Claim' as never))
      expect(screen.queryByRole('button', { name: 'Release' }) !== null).toBe(buttons.includes('Release' as never))
      expect(screen.queryByRole('button', { name: 'Reassign…' }) !== null).toBe(buttons.includes('Reassign…' as never))
      expect(screen.getAllByRole('button')).toHaveLength(buttons.length + 3)
      unmount()
    }
  })

  it('claims the task, shows Release from the answer and keeps what the user typed', async () => {
    const api = stubTaskApi({ reads: [{ status: 200, body: { ...openTask, commands: ['claim'] } }] })
    renderPage()

    await userEvent.type(await screen.findByLabelText('Comments'), 'Fine')
    await userEvent.click(screen.getByRole('button', { name: 'Claim' }))

    expect(await screen.findByRole('button', { name: 'Release' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Claim' })).not.toBeInTheDocument()
    expect(api.commands()).toEqual([`${tasksPath}/${taskId}/claim`])
    expect(screen.getByLabelText('Comments')).toHaveValue('Fine')
    expect(api.subjectReads()).toHaveLength(1)
    expect(api.taskReads()).toHaveLength(1)
  })

  it('releases the task and shows the commands of the answer', async () => {
    const api = stubTaskApi({
      reads: [{ status: 200, body: { ...openTask, commands: ['release'] } }],
      command: { status: 200, body: { ...openTask, commands: ['claim'] } },
    })
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Release' }))

    expect(await screen.findByRole('button', { name: 'Claim' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Release' })).not.toBeInTheDocument()
    expect(api.commands()).toEqual([`${tasksPath}/${taskId}/release`])
  })

  it('shows a message and loads the task again when claim answers 409 or 403', async () => {
    for (const status of [409, 403]) {
      const api = stubTaskApi({
        reads: [
          { status: 200, body: { ...openTask, commands: ['claim'] } },
          { status: 200, body: { ...openTask, commands: [] } },
        ],
        command: { status, body: { title: 'Refused.', status } },
      })
      const { unmount } = renderPage()

      await userEvent.click(await screen.findByRole('button', { name: 'Claim' }))

      expect(await screen.findByTestId('task-command-message')).toHaveTextContent(
        'This action is no longer possible. The task was loaded again.',
      )
      await waitFor(() => expect(api.taskReads()).toHaveLength(2))
      await waitFor(() => expect(screen.queryByRole('button', { name: 'Claim' })).not.toBeInTheDocument())
      unmount()
    }
  })

  describe('saving', () => {
    const afterSave = JSON.stringify({
      id: subjectId,
      version: 4,
      values: { title: 'Laptops for Q4', decisionComments: 'Fine' },
      labels: {},
    })

    async function typeComment() {
      await userEvent.type(await screen.findByLabelText('Comments'), 'Fine')
    }

    it('shows the save bar for a change, and Save posts the changed values with the version and keeps the task open', async () => {
      const api = stubTaskApi({ subjects: [subject, afterSave] })
      renderPage()
      await screen.findByLabelText('Comments')
      expect(screen.queryByTestId('task-save-bar')).not.toBeInTheDocument()

      await typeComment()
      expect(await screen.findByTestId('task-save-bar')).toBeInTheDocument()
      await userEvent.click(screen.getByTestId('task-save'))

      await waitFor(() => expect(screen.queryByTestId('task-save-bar')).not.toBeInTheDocument())
      expect(api.saves()).toEqual(['{"version":3,"values":{"decisionComments":"Fine"}}'])
      expect(api.completions()).toHaveLength(0)
      expect(screen.getByTestId('location').textContent).toBe(`/e2e/tasks/${taskId}`)
      expect(api.subjectReads()).toHaveLength(2)
      expect(screen.getByLabelText('Comments')).toHaveValue('Fine')
      expect(
        within(within(screen.getByTestId('page-actions')).getByRole('group', { name: 'Decision' }))
          .getAllByRole('button')
          .map((button) => button.textContent),
      ).toEqual(['Approve', 'Return', 'Reject'])
    })

    it('sends the saved record version with a later outcome', async () => {
      const api = stubTaskApi({ subjects: [subject, afterSave] })
      renderPage()
      await typeComment()
      await userEvent.click(await screen.findByTestId('task-save'))
      await waitFor(() => expect(screen.queryByTestId('task-save-bar')).not.toBeInTheDocument())

      await userEvent.click(screen.getByRole('button', { name: 'Approve' }))

      await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/e2e/tasks'))
      expect(api.completions().map(({ body }) => body)).toEqual(['{"outcome":"approve","version":4,"values":{}}'])
    })

    it('asks before Discard, then puts back the loaded value and hides the save bar', async () => {
      const api = stubTaskApi()
      renderPage()
      const comments = await screen.findByLabelText('Comments')

      await userEvent.type(comments, 'Fine')
      await userEvent.click(within(await screen.findByTestId('task-save-bar')).getByRole('button', { name: 'Discard' }))
      expect(await screen.findByText('Discard all unsaved changes?')).toBeInTheDocument()
      expect(comments).toHaveValue('Fine')
      await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Discard' }))

      await waitFor(() => expect(comments).toHaveValue(''))
      expect(screen.queryByTestId('task-save-bar')).not.toBeInTheDocument()
      expect(api.saves()).toEqual([])
    })

    it('shows a 400 under the field it names', async () => {
      stubTaskApi({
        save: {
          status: 400,
          body: {
            title: 'Invalid.',
            status: 400,
            errors: { '/values/decisionComments': ['Too long.'] },
          },
        },
      })
      renderPage()
      await typeComment()

      await userEvent.click(await screen.findByTestId('task-save'))

      expect(await screen.findByText('Too long.')).toBeInTheDocument()
      expect(screen.getByLabelText('Comments')).toHaveValue('Fine')
      expect(screen.getByTestId('task-save-bar')).toBeInTheDocument()
    })

    it('shows a 409 that names a key above the form', async () => {
      stubTaskApi({
        save: {
          status: 409,
          body: {
            title: 'The record has changed since this version was read.',
            status: 409,
            errors: { '/version': ["Must be the record's current version."] },
          },
        },
      })
      renderPage()
      await typeComment()

      await userEvent.click(await screen.findByTestId('task-save'))

      expect(await screen.findByTestId('form-error')).toHaveTextContent("Must be the record's current version.")
    })

    it('shows that the task is completed on a 409 with no keys', async () => {
      stubTaskApi({
        reads: [
          { status: 200, body: openTask },
          { status: 200, body: completedTask },
        ],
        save: { status: 409, body: { title: 'This task is already completed.', status: 409 } },
      })
      renderPage()
      await typeComment()

      await userEvent.click(await screen.findByTestId('task-save'))

      expect(await screen.findByTestId('task-completed')).toBeInTheDocument()
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    })

    it('shows that the task is assigned to someone else on a 403', async () => {
      stubTaskApi({ save: { status: 403, body: { title: 'This task is assigned to someone else.', status: 403 } } })
      renderPage()
      await typeComment()

      await userEvent.click(await screen.findByTestId('task-save'))

      expect(await screen.findByTestId('task-assigned-elsewhere')).toBeInTheDocument()
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    })

    it('shows that the save failed on any other answer, and keeps the change', async () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})
      stubTaskApi({ save: { status: 500 } })
      renderPage()
      await typeComment()

      await userEvent.click(await screen.findByTestId('task-save'))

      expect(await screen.findByTestId('form-error')).toHaveTextContent('The record could not be saved.')
      expect(screen.getByTestId('task-save-bar')).toBeInTheDocument()
    })

    it('shows that the record could not be loaded when the read after a save fails', async () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})
      const api = stubTaskApi()
      renderPage()
      await typeComment()
      const read = vi.mocked(fetch).getMockImplementation()!
      vi.mocked(fetch).mockImplementation(async (url, init) =>
        url === `${recordsPath}/${subjectId}` ? new Response('', { status: 500 }) : read(url, init),
      )

      await userEvent.click(await screen.findByTestId('task-save'))

      expect(await screen.findByTestId('form-error')).toHaveTextContent('The record could not be loaded.')
      expect(api.saves()).toHaveLength(1)
    })

    it('shows the save bar on the last step of a wizard only, and sends the changes of every step', async () => {
      const api = stubTaskApi({
        reads: [{ status: 200, body: stepsTask }],
        subjects: [subject, afterSave],
      })
      renderPage()

      await userEvent.type(await screen.findByLabelText('Comments'), 'Fine')
      expect(screen.queryByTestId('task-save-bar')).not.toBeInTheDocument()
      await userEvent.click(screen.getByRole('button', { name: 'Next' }))
      await userEvent.click(await screen.findByTestId('task-save'))

      await waitFor(() => expect(api.saves()).toEqual(['{"version":3,"values":{"decisionComments":"Fine"}}']))
    })

    describe('under the guard of unsaved changes', () => {
      it('asks "Leave without saving?" before a link after a change, and not before one', async () => {
        stubTaskApi()
        const router = renderGuardedPage()
        await screen.findByLabelText('Comments')

        await followLink()
        expect(router.state.location.pathname).toBe('/elsewhere')
        await router.navigate('/studio')

        await userEvent.type(screen.getByLabelText('Comments'), 'Fine')
        await followLink()

        expect(await screen.findByText('Leave without saving?')).toBeInTheDocument()
        expect(router.state.location.pathname).toBe('/studio')
      })

      it('goes to the inbox with no prompt when an outcome is chosen after a change', async () => {
        stubTaskApi()
        const router = renderGuardedPage()

        await userEvent.type(await screen.findByLabelText('Comments'), 'Fine')
        await userEvent.click(screen.getByRole('button', { name: 'Approve' }))

        await waitFor(() => expect(router.state.location.pathname).toBe('/e2e/tasks'))
        expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
      })

      it('goes to the inbox with no prompt when the task is reassigned after a change', async () => {
        stubTaskApi({
          reads: [
            {
              status: 200,
              body: { ...openTask, commands: ['reassign'], reassignRoles: [{ name: 'procurement', labelKey: null }] },
            },
          ],
        })
        const router = renderGuardedPage()
        await userEvent.type(await screen.findByLabelText('Comments'), 'Fine')

        await userEvent.click(screen.getByRole('button', { name: 'Reassign…' }))
        const dialog = await screen.findByRole('dialog')
        await userEvent.click(within(dialog).getByRole('combobox', { name: 'Role' }))
        await userEvent.click(await screen.findByTitle('procurement'))
        await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Agent on leave.')
        await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

        await waitFor(() => expect(router.state.location.pathname).toBe('/e2e/tasks'))
        expect(screen.queryByText('Leave without saving?')).not.toBeInTheDocument()
      })
    })
  })

  describe('reassign', () => {
    const roles = [
      { name: 'procurement', labelKey: null },
      { name: 'finance', labelKey: 'role.finance' },
    ]
    const reassignable = { ...openTask, commands: ['reassign'], reassignRoles: roles }
    const reads = [{ status: 200, body: reassignable }]
    const people = {
      status: 200,
      body: {
        items: [
          { id: 'u-ann', displayName: 'Ann Lee' },
          { id: 'u-bao', displayName: 'Bao Tran' },
        ],
      },
    }

    async function openDialog() {
      await userEvent.click(await screen.findByRole('button', { name: 'Reassign…' }))
      return screen.findByRole('dialog')
    }

    it('gives each field of the dialog its help', async () => {
      stubTaskApi({ reads })
      renderPage()
      const dialog = await openDialog()

      expect(within(dialog).getByRole('radiogroup', { name: 'Reassign to' })).toHaveAccessibleDescription(
        platformEnglish['shell.task.reassignTarget.help'],
      )
      expect(within(dialog).getByRole('combobox', { name: 'Role' })).toHaveAccessibleDescription(
        platformEnglish['shell.task.reassignRole.help'],
      )
      expect(within(dialog).getByRole('textbox', { name: 'Reason' })).toHaveAccessibleDescription(
        platformEnglish['shell.task.reassignReason.help'],
      )

      await userEvent.click(within(dialog).getByRole('radio', { name: 'Person' }))
      expect(within(dialog).getByRole('combobox', { name: 'Person' })).toHaveAccessibleDescription(
        platformEnglish['shell.task.reassignPerson.help'],
      )
    })

    it('sends the chosen role with the reason and goes to the inbox', async () => {
      const api = stubTaskApi({ reads })
      renderPage()
      const dialog = await openDialog()

      await userEvent.click(within(dialog).getByRole('combobox', { name: 'Role' }))
      expect(await screen.findByTitle('procurement')).toBeInTheDocument()
      expect(screen.getByTitle('Finance team')).toBeInTheDocument()
      await userEvent.click(screen.getByTitle('procurement'))
      await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Agent on leave.')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

      await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/e2e\/tasks$/))
      expect(api.reassigns()).toEqual([{ role: 'procurement', reason: 'Agent on leave.' }])
    })

    it('sends the person found by name with the reason', async () => {
      const api = stubTaskApi({
        reads,
        users: (search) => (search === 'bao' ? { status: 200, body: { items: [people.body.items[1]] } } : people),
      })
      renderPage()
      const dialog = await openDialog()

      await userEvent.click(within(dialog).getByRole('radio', { name: 'Person' }))
      const picker = within(dialog).getByRole('combobox', { name: 'Person' })
      await userEvent.click(picker)
      expect(await screen.findByTitle('Ann Lee')).toBeInTheDocument()
      await userEvent.type(picker, 'bao')
      await waitFor(() => expect(screen.queryByTitle('Ann Lee')).not.toBeInTheDocument())
      await userEvent.click(await screen.findByTitle('Bao Tran'))
      await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Knows the vendor.')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

      await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent(/^\/e2e\/tasks$/))
      expect(api.reassigns()).toEqual([{ user: 'u-bao', reason: 'Knows the vendor.' }])
      expect(api.userSearches()).toEqual(['', 'b', 'ba', 'bao'])
    })

    it('needs a target and a reason before it sends', async () => {
      const api = stubTaskApi({ reads })
      renderPage()
      const dialog = await openDialog()

      await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

      expect(await within(dialog).findByText('Choose who gets the task.')).toBeInTheDocument()
      expect(within(dialog).getByText('Enter a reason.')).toBeInTheDocument()
      expect(api.reassigns()).toEqual([])
    })

    it('puts the focus on the first empty field, the role, and sends nothing', async () => {
      const api = stubTaskApi({ reads })
      renderPage()
      const dialog = await openDialog()

      await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

      expect(await within(dialog).findByText('Choose who gets the task.')).toBeInTheDocument()
      expect(within(dialog).getByRole('combobox', { name: 'Role' })).toHaveFocus()
      expect(api.reassigns()).toEqual([])
    })

    it('shows a 400 under the field it names, with the dialog still open', async () => {
      for (const [kind, pointer] of [
        ['role', '/reason'],
        ['user', '/user'],
      ] as const) {
        const api = stubTaskApi({
          reads,
          users: people,
          reassign: {
            status: 400,
            body: { title: 'Invalid.', status: 400, errors: { [pointer]: ['Not allowed here.'] } },
          },
        })
        const { unmount } = renderPage()
        const dialog = await openDialog()

        if (kind === 'user') {
          await userEvent.click(within(dialog).getByRole('radio', { name: 'Person' }))
          await userEvent.click(within(dialog).getByRole('combobox', { name: 'Person' }))
          await userEvent.click(await screen.findByTitle('Ann Lee'))
        } else {
          await userEvent.click(within(dialog).getByRole('combobox', { name: 'Role' }))
          await userEvent.click(await screen.findByTitle('procurement'))
        }
        await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Because.')
        await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

        const message = await within(dialog).findByText('Not allowed here.')
        const field =
          kind === 'user'
            ? within(dialog).getByRole('combobox', { name: 'Person' })
            : within(dialog).getByRole('textbox', { name: 'Reason' })
        expect(message.closest('.ant-form-item')).toContainElement(field)
        expect(screen.getByRole('dialog')).toBeInTheDocument()
        expect(screen.getByTestId('location')).toHaveTextContent(`/e2e/tasks/${taskId}`)
        expect(api.reassigns()).toHaveLength(1)
        unmount()
      }
    })

    it('shows a problem on the whole body above the form', async () => {
      stubTaskApi({
        reads,
        reassign: { status: 400, body: { title: 'Invalid.', status: 400, errors: { '': ['Choose one target.'] } } },
      })
      renderPage()
      const dialog = await openDialog()

      await userEvent.click(within(dialog).getByRole('combobox', { name: 'Role' }))
      await userEvent.click(await screen.findByTitle('procurement'))
      await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Because.')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

      expect(await within(dialog).findByTestId('task-reassign-error')).toHaveTextContent('Choose one target.')
    })

    it('shows a message on the role above the form while Person is chosen', async () => {
      stubTaskApi({
        reads,
        users: people,
        reassign: { status: 400, body: { title: 'Invalid.', status: 400, errors: { '/role': ['Not a role.'] } } },
      })
      renderPage()
      const dialog = await openDialog()

      await userEvent.click(within(dialog).getByRole('radio', { name: 'Person' }))
      await userEvent.click(within(dialog).getByRole('combobox', { name: 'Person' }))
      await userEvent.click(await screen.findByTitle('Ann Lee'))
      await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Because.')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

      expect(await within(dialog).findByTestId('task-reassign-error')).toHaveTextContent('Not a role.')
    })

    it('closes the dialog, loads the task again and shows the refusal on a 409 or 403', async () => {
      for (const status of [409, 403]) {
        const api = stubTaskApi({
          reads: [reads[0], { status: 200, body: { ...openTask, commands: [] } }],
          reassign: { status, body: { title: 'Refused.', status } },
        })
        const { unmount } = renderPage()
        const dialog = await openDialog()

        await userEvent.click(within(dialog).getByRole('combobox', { name: 'Role' }))
        await userEvent.click(await screen.findByTitle('procurement'))
        await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Because.')
        await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

        await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
        expect(await screen.findByTestId('task-command-message')).toHaveTextContent(
          'This action is no longer possible. The task was loaded again.',
        )
        await waitFor(() => expect(api.taskReads()).toHaveLength(2))
        await waitFor(() => expect(screen.queryByRole('button', { name: 'Reassign…' })).not.toBeInTheDocument())
        unmount()
      }
    })

    it('shows a generic message in the open dialog when reassigning fails otherwise', async () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})
      stubTaskApi({ reads, reassign: { status: 500 } })
      renderPage()
      const dialog = await openDialog()

      await userEvent.click(within(dialog).getByRole('combobox', { name: 'Role' }))
      await userEvent.click(await screen.findByTitle('procurement'))
      await userEvent.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Because.')
      await userEvent.click(within(dialog).getByRole('button', { name: 'Reassign' }))

      expect(await within(dialog).findByTestId('task-reassign-error')).toHaveTextContent(
        'The action could not be done.',
      )
      expect(within(dialog).getByRole('textbox', { name: 'Reason' })).toHaveValue('Because.')
    })

    it('keeps the dialog usable when the people cannot be listed', async () => {
      vi.spyOn(console, 'warn').mockImplementation(() => {})
      stubTaskApi({ reads, users: { status: 403 } })
      renderPage()
      const dialog = await openDialog()

      await userEvent.click(within(dialog).getByRole('radio', { name: 'Person' }))
      await userEvent.click(within(dialog).getByRole('radio', { name: 'Role' }))

      expect(within(dialog).getByRole('combobox', { name: 'Role' })).toBeInTheDocument()
    })
  })

  it('shows a generic message when claim fails otherwise', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const api = stubTaskApi({
      reads: [{ status: 200, body: { ...openTask, commands: ['claim'] } }],
      command: { status: 500 },
    })
    renderPage()

    await userEvent.click(await screen.findByRole('button', { name: 'Claim' }))

    expect(await screen.findByTestId('task-command-message')).toHaveTextContent('The action could not be done.')
    expect(api.taskReads()).toHaveLength(1)
  })

  it('shows only the expired message for an expired task', async () => {
    stubTaskApi({ reads: [{ status: 200, body: { ...expiredTask, commands: ['claim', 'release'] } }] })

    renderPage()

    expect(await screen.findByTestId('task-expired')).toHaveTextContent('This task is past its deadline.')
    expect(screen.queryByTestId('task-completed')).not.toBeInTheDocument()
    expect(screen.getByText('Manager approval')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('shows the not-found line under the inbox heading, with a link to the inbox, when the task API answers 404', async () => {
    stubTaskApi({ reads: [{ status: 404, body: { title: 'No task exists with this id.', status: 404 } }] })

    renderPage()

    const line = await screen.findByTestId('record-not-found')
    expect(line).toHaveTextContent('This task does not exist, or you cannot open it.')
    expect(within(line).getByRole('link', { name: 'Go to Tasks' })).toHaveAttribute('href', '/e2e/tasks')
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Tasks')
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('asks to sign in when nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    history.pushState(null, '', `/e2e/tasks/${taskId}?from=inbox`)
    stubTaskApi({ reads: [{ status: 401, body: { title: 'Sign in to read or write this data.', status: 401 } }] })

    renderPage()

    await waitFor(() => expect(isSignInNeeded()).toBe(true))
    expect(assignLocation).not.toHaveBeenCalled()
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

  describe('a form with steps', () => {
    const commentsRequired = {
      sections: [],
      fields: {},
      errors: { '/values/decisionComments': ['Required.'] },
    }
    const noErrors = { sections: [], fields: {}, errors: {} }

    it('shows the outcomes only on the last step, and Next stays on a step with an error', async () => {
      const api = stubTaskApi({
        reads: [{ status: 200, body: stepsTask }],
        states: [noErrors, commentsRequired, noErrors],
      })
      renderPage()

      expect(await screen.findByLabelText('Comments')).toBeInTheDocument()
      expect(screen.queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()
      expect(screen.getByTestId('steps-bar')).toHaveTextContent('Step one')
      expect(screen.getByTestId('steps-bar')).toHaveTextContent('Step two')
      expect(screen.getAllByRole('button').map((button) => button.textContent)).toEqual(['Next'])
      await waitFor(() => expect(api.stateRequests()).toHaveLength(1))

      await userEvent.click(screen.getByRole('button', { name: 'Next' }))
      expect(await screen.findByTestId('field-error-decisionComments')).toHaveTextContent('Required.')
      expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
      expect(api.stateRequests()[1].body).toEqual({ values: {}, touched: ['decisionComments'] })

      await userEvent.type(screen.getByLabelText('Comments'), 'Fine')
      await userEvent.click(screen.getByRole('button', { name: 'Next' }))
      expect(await screen.findByTestId('field-value-title')).toBeInTheDocument()
      expect(screen.queryByRole('textbox', { name: 'Title' })).not.toBeInTheDocument()
      // The outcomes are on the heading, so Back is the only button under the fields. The save bar is not under them.
      expect(
        within(screen.getByTestId('page-actions'))
          .getAllByRole('button')
          .map((button) => button.textContent),
      ).toEqual(['Approve', 'Return', 'Reject'])
      expect(
        within(screen.getByTestId('task-form'))
          .getAllByRole('button')
          .filter((button) => !screen.getByTestId('task-save-bar').contains(button))
          .map((button) => button.textContent),
      ).toEqual(['Back'])

      await userEvent.click(screen.getByRole('button', { name: 'Back' }))
      expect(await screen.findByLabelText('Comments')).toHaveValue('Fine')
    })

    it('keeps Back and Next under the fields and the outcomes off the heading before the last step', async () => {
      stubTaskApi({ reads: [{ status: 200, body: stepsTask }] })
      const { container } = renderPage()

      const comments = await screen.findByLabelText('Comments')
      const form = container.querySelector('form')!
      const next = within(screen.getByTestId('task-form')).getByRole('button', { name: 'Next' })
      expect(form).toContainElement(comments)
      expect(form.compareDocumentPosition(next) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(within(screen.getByTestId('page-actions')).queryByRole('button')).not.toBeInTheDocument()
      expect(screen.queryByRole('group', { name: 'Decision' })).not.toBeInTheDocument()
    })

    it('completes with the changes from every step', async () => {
      const api = stubTaskApi({ reads: [{ status: 200, body: stepsTask }] })
      renderPage()

      await userEvent.type(await screen.findByLabelText('Comments'), 'Fine')
      await userEvent.click(screen.getByRole('button', { name: 'Next' }))
      await userEvent.click(await screen.findByRole('button', { name: 'Approve' }))

      await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/e2e/tasks'))
      expect(api.completions().map(({ body }) => body)).toEqual([
        '{"outcome":"approve","version":3,"values":{"decisionComments":"Fine"}}',
      ])
    })
  })
})
