import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { formItem } from '../test/antd'
import { assignLocation } from './navigation'
import { ProcessInstancePage } from './ProcessInstancePage'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'

vi.mock('./navigation')

const instancesPath = '/api/apps/PurchasingApp/processes/PurchaseRequestApproval/instances'
const instanceId = '0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b'
const releaseId = '0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01'

const inspectorWidget: WidgetMetadata = {
  type: 'processInspector',
  formPage: null,
  entity: null,
  dataSource: null,
  form: null,
  tasksPath: null,
  process: 'PurchaseRequestApproval',
  instancesPath,
  actions: [],
  rowActions: [],
  toolbarActions: [],
  parameters: {},
  access: null,
}

// The instance detail example of the processes reference, with an open task and two calls. The
// source text of the visible input keeps its decimal digits.
const detailText = `{
  "id": "${instanceId}",
  "process": "PurchaseRequestApproval",
  "subjectId": "0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b",
  "releaseId": "${releaseId}",
  "state": "waiting",
  "step": "registerPurchase",
  "createdAt": "2026-10-10T02:05:00.123456Z",
  "endedAt": null,
  "graph": {
    "start": "managerApproval",
    "steps": [
      {
        "name": "managerApproval",
        "type": "task",
        "labelKey": "purchaseRequest.managerApproval",
        "transitions": [
          { "next": "registerPurchase", "on": "approve" },
          { "next": "done", "on": "reject" }
        ]
      },
      {
        "name": "registerPurchase",
        "type": "operation",
        "labelKey": null,
        "transitions": [{ "next": "done", "on": null }]
      },
      { "name": "done", "type": "end", "labelKey": null, "transitions": [] }
    ]
  },
  "path": [
    {
      "step": "managerApproval",
      "startedAt": "2026-10-10T02:05:00.123456Z",
      "finishedAt": "2026-10-10T02:05:00.130000Z",
      "decision": null,
      "output": { "state": "waiting", "taskId": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01" },
      "error": null
    }
  ],
  "tasks": [
    {
      "id": "0192f4b1-7a2c-7d3e-8f40-5b6c7d8e9f01",
      "step": "managerApproval",
      "assigneeKind": "role",
      "assignee": "manager",
      "state": "open",
      "outcome": null,
      "completedBy": null,
      "createdAt": "2026-10-10T02:05:00.123456Z",
      "completedAt": null
    }
  ],
  "calls": [
    {
      "id": "0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c",
      "kind": "connector",
      "step": "registerPurchase",
      "connector": "PurchasingSystem",
      "operation": "createOrder",
      "state": "pending",
      "attempts": [
        {
          "kind": "attempt",
          "number": 1,
          "startedAt": "2026-10-10T04:00:01.000000Z",
          "finishedAt": "2026-10-10T04:00:03.000000Z",
          "outcome": "retryable",
          "httpStatus": 503,
          "error": null
        }
      ],
      "input": null,
      "inputHidden": true,
      "resolvable": false
    },
    {
      "id": "0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8d",
      "kind": "package",
      "step": "registerPurchase",
      "package": "PaymentReferences",
      "version": "1.0.0",
      "idempotent": false,
      "operation": "reference",
      "state": "succeeded",
      "attempts": [],
      "input": { "title": "Laptops", "amount": 1250.50, "lines": ["a", "b"] },
      "inputHidden": false,
      "resolvable": false
    }
  ],
  "commands": []
}`

const texts = {
  ...platformEnglish,
  'purchaseRequest.managerApproval': 'Manager approval',
}
const catalogs = [{ texts, fallbackTexts: texts }]

interface Answer {
  status: number
  body?: string
}

/** The detail text with other `commands`, and with its package call and its connector call failed. */
function detailWith(commands: string[], failed?: 'package' | 'connector') {
  const detail = JSON.parse(detailText) as {
    commands: string[]
    state: string
    calls: { kind: string; state: string }[]
  }
  detail.commands = commands
  if (failed) {
    detail.state = 'failed'
    for (const call of detail.calls) {
      call.state = call.kind === failed ? 'failed' : 'succeeded'
    }
  }
  return JSON.stringify(detail)
}

const resolvableCallId = '0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c'

/** The detail text with its first call `unknown` and resolvable, with three declared outputs. */
function detailWithResolvable() {
  const detail = JSON.parse(detailText) as {
    calls: { state: string; resolvable: boolean; outputs?: { name: string; type: string; required: boolean }[] }[]
  }
  detail.calls[0].state = 'unknown'
  detail.calls[0].resolvable = true
  detail.calls[0].outputs = [
    { name: 'orderNumber', type: 'text', required: true },
    { name: 'total', type: 'decimal', required: false },
    { name: 'confirmed', type: 'boolean', required: false },
  ]
  return JSON.stringify(detail)
}

function stubApi({
  detail = { status: 200, body: detailText },
  command = { status: 200, body: '{}' },
  resolve = { status: 200, body: '{}' },
  reloaded,
  releaseTexts = { status: 200, body: JSON.stringify({ locale: 'en', texts: {} }) },
}: { detail?: Answer; command?: Answer; resolve?: Answer; reloaded?: Answer; releaseTexts?: Answer } = {}) {
  let reads = 0
  const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (url === `${instancesPath}/${instanceId}`) {
      reads += 1
      const answer = reads > 1 && reloaded ? reloaded : detail
      return new Response(answer.body ?? '', { status: answer.status })
    }
    if (init?.method === 'POST' && (url.endsWith('/cancel') || url.endsWith('/retry'))) {
      return new Response(command.body ?? '', { status: command.status })
    }
    if (init?.method === 'POST' && url.endsWith('/resolve')) {
      return new Response(resolve.body ?? '', { status: resolve.status })
    }
    if (url === `/api/sites/e2e/texts/en?release=${releaseId}`) {
      return new Response(releaseTexts.body ?? '', { status: releaseTexts.status })
    }
    return new Response('', { status: 404 })
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderPage() {
  return render(
    <MemoryRouter initialEntries={[`/e2e/inspector/${instanceId}`]}>
      <TextProvider catalogs={catalogs} development={false}>
        <ProcessInstancePage
          sitePath="e2e"
          widget={inspectorWidget}
          instanceId={instanceId}
          locale="en"
          fallbackLocale="en"
        />
      </TextProvider>
    </MemoryRouter>,
  )
}

describe('ProcessInstancePage', () => {
  it('lists the steps in graph order with the taken and current marks, and shows the open task', async () => {
    stubApi()

    renderPage()

    await screen.findByTestId('inspector-instance')
    const steps = screen.getAllByTestId(/^inspector-step-/)
    expect(steps.map((step) => step.getAttribute('data-testid'))).toEqual([
      'inspector-step-managerApproval',
      'inspector-step-registerPurchase',
      'inspector-step-done',
    ])
    const [approval, register, done] = steps
    expect(within(approval).getByText('Manager approval')).toBeInTheDocument()
    expect(within(approval).getByText('Taken')).toBeInTheDocument()
    expect(within(approval).queryByText('Current')).not.toBeInTheDocument()
    expect(within(approval).getByText('→ registerPurchase (approve)')).toBeInTheDocument()
    expect(within(approval).getByText('→ done (reject)')).toBeInTheDocument()
    expect(within(register).getByText('Current')).toBeInTheDocument()
    expect(within(register).queryByText('Taken')).not.toBeInTheDocument()
    expect(within(register).getByText('→ done')).toBeInTheDocument()
    expect(within(done).queryByText('Taken')).not.toBeInTheDocument()
    expect(within(done).queryByText('Current')).not.toBeInTheDocument()

    const taskRow = screen.getByRole('row', { name: /role: manager/ })
    expect(within(taskRow).getByText('Manager approval')).toBeInTheDocument()
    expect(within(taskRow).getByText('open')).toBeInTheDocument()
  })

  it('shows the history rows and the attempts of a call', async () => {
    stubApi()

    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByText('No history yet.')).not.toBeInTheDocument()
    const attemptRow = screen.getByRole('row', { name: /retryable/ })
    expect(within(attemptRow).getByText('503')).toBeInTheDocument()
    expect(within(attemptRow).getByText('attempt')).toBeInTheDocument()
  })

  it('shows the hidden text and no input value for a call whose input is hidden', async () => {
    stubApi()

    renderPage()

    await screen.findByTestId('inspector-instance')
    const hidden = screen.getByTestId('inspector-call-0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8c')
    expect(within(hidden).getByText('Input: Hidden')).toBeInTheDocument()
    expect(within(hidden).getByText('PurchasingSystem · createOrder')).toBeInTheDocument()
    expect(within(hidden).queryByText('title')).not.toBeInTheDocument()
  })

  it('shows the top-level input properties of a visible input, with numbers as their source digits', async () => {
    stubApi()

    renderPage()

    await screen.findByTestId('inspector-instance')
    const visible = screen.getByTestId('inspector-call-0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8d')
    expect(within(visible).getByText('PaymentReferences@1.0.0 · reference')).toBeInTheDocument()
    expect(within(visible).queryByText('Hidden')).not.toBeInTheDocument()
    expect(within(visible).getByText('Laptops')).toBeInTheDocument()
    expect(within(visible).getByText('1250.50')).toBeInTheDocument()
    expect(within(visible).getByText('["a","b"]')).toBeInTheDocument()
  })

  it('shows a step label from the texts of the instance release, and the name when there is none', async () => {
    stubApi({
      releaseTexts: {
        status: 200,
        body: JSON.stringify({ locale: 'en', texts: { 'purchaseRequest.managerApproval': 'Approval by the manager' } }),
      },
    })

    renderPage()

    await screen.findByTestId('inspector-instance')
    const approval = screen.getByTestId('inspector-step-managerApproval')
    expect(within(approval).getByText('Approval by the manager')).toBeInTheDocument()
    expect(within(screen.getByTestId('inspector-step-done')).getByText('done')).toBeInTheDocument()
  })

  it('shows the no-such-instance message on a 404', async () => {
    stubApi({ detail: { status: 404 } })

    renderPage()

    expect(await screen.findByTestId('inspector-not-found')).toHaveTextContent(
      'No instance of this process exists with this id.',
    )
    expect(screen.queryByTestId('inspector-instance')).not.toBeInTheDocument()
  })

  it('shows the may-not-inspect message on a 403', async () => {
    stubApi({ detail: { status: 403 } })

    renderPage()

    expect(await screen.findByTestId('inspector-forbidden')).toHaveTextContent('You may not inspect this process.')
  })

  it('shows a load error on any other failure, and when the release texts fail to load', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubApi({ detail: { status: 500 } })
    const { unmount } = renderPage()

    expect(await screen.findByTestId('inspector-error')).toBeInTheDocument()
    unmount()

    stubApi({ releaseTexts: { status: 500 } })
    renderPage()

    expect(await screen.findByTestId('inspector-error')).toBeInTheDocument()
    expect(screen.queryByTestId('inspector-instance')).not.toBeInTheDocument()
  })

  it('starts sign-in when nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    stubApi({ detail: { status: 401 } })

    renderPage()

    await waitFor(() => expect(assignLocation).toHaveBeenCalledOnce())
  })

  it('shows only the command buttons the detail allows', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['cancel']) } })
    const { unmount } = renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.getByTestId('inspector-command-cancel')).toHaveTextContent('Cancel instance')
    expect(screen.queryByTestId('inspector-command-retry')).not.toBeInTheDocument()
    unmount()

    stubApi({ detail: { status: 200, body: detailWith([]) } })
    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByTestId('inspector-command-cancel')).not.toBeInTheDocument()
    expect(screen.queryByTestId('inspector-command-retry')).not.toBeInTheDocument()
  })

  it('leaves out a command this build does not know', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['resolve']) } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByTestId(/^inspector-command-/)).not.toBeInTheDocument()
  })

  it('warns before a retry of a failed non-idempotent package call', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['retry'], 'package') } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-retry'))

    expect(await screen.findByTestId('inspector-retry-warning')).toBeInTheDocument()
  })

  it('shows no retry warning for a failed connector call, or for the cancel dialog', async () => {
    stubApi({ detail: { status: 200, body: detailWith(['retry', 'cancel'], 'connector') } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-retry'))
    await screen.findByRole('dialog')
    expect(screen.queryByTestId('inspector-retry-warning')).not.toBeInTheDocument()
  })

  it('posts the reason of a cancel and shows the instance read again', async () => {
    const cancelled = JSON.parse(detailWith([])) as { state: string }
    cancelled.state = 'cancelled'
    const fetchMock = stubApi({
      detail: { status: 200, body: detailWith(['cancel']) },
      reloaded: { status: 200, body: JSON.stringify(cancelled) },
    })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Close')).toBeInTheDocument()
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Requested by finance' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByTestId('inspector-command-cancel')).not.toBeInTheDocument())
    expect(within(screen.getByTestId('inspector-instance')).getByText('Cancelled')).toBeInTheDocument()
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(post[0]).toBe(`${instancesPath}/${instanceId}/cancel`)
    expect(JSON.parse(post[1]!.body as string)).toEqual({ reason: 'Requested by finance' })
  })

  it('blocks a blank reason and sends nothing', async () => {
    const fetchMock = stubApi({ detail: { status: 200, body: detailWith(['cancel']) } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: '   ' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)
  })

  it('shows the problem title of a refused command in the dialog', async () => {
    stubApi({
      detail: { status: 200, body: detailWith(['cancel']) },
      command: { status: 409, body: JSON.stringify({ title: 'The instance already ended.' }) },
    })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Too late' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByTestId('inspector-command-error')).toHaveTextContent(
      'The instance already ended.',
    )
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('shows a generic error when the command fails any other way', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubApi({ detail: { status: 200, body: detailWith(['cancel']) }, command: { status: 500 } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId('inspector-command-cancel'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Try' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByTestId('inspector-command-error')).toHaveTextContent(
      'The command could not be sent.',
    )
  })

  it('posts a resolve as succeeded with the reason and the typed outputs', async () => {
    const fetchMock = stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'The order exists' } })
    await userEvent.click(within(dialog).getByLabelText('Succeeded'))
    fireEvent.change(within(dialog).getByLabelText('orderNumber (text)'), { target: { value: 'PO-2026-0001' } })
    fireEvent.change(within(dialog).getByLabelText('total (decimal)'), { target: { value: '1250.50' } })
    await userEvent.click(within(dialog).getByLabelText('confirmed (boolean)'))
    await userEvent.click(await screen.findByTitle('true'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(post[0]).toBe(`${instancesPath}/${instanceId}/calls/${resolvableCallId}/resolve`)
    const body = post[1]!.body as string
    expect(body).toContain('"total":1250.50')
    expect(JSON.parse(body)).toEqual({
      reason: 'The order exists',
      outcome: 'succeeded',
      output: { orderNumber: 'PO-2026-0001', total: 1250.5, confirmed: true },
    })
  })

  it('sends a failed resolve without outputs, and blocks a missing outcome or required output', async () => {
    const fetchMock = stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'No order was found' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))
    expect(await within(dialog).findByText('Choose an outcome.')).toBeInTheDocument()

    await userEvent.click(within(dialog).getByLabelText('Succeeded'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))
    expect(await within(dialog).findByText('Enter a value.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'POST')).toBe(false)

    await userEvent.click(within(dialog).getByLabelText('Failed'))
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(JSON.parse(post[1]!.body as string)).toEqual({ reason: 'No order was found', outcome: 'failed' })
  })

  it('shows the message of a 400 under the output field it names, and keeps the dialog open', async () => {
    stubApi({
      detail: { status: 200, body: detailWithResolvable() },
      resolve: {
        status: 400,
        body: JSON.stringify({
          title: 'The request has errors.',
          errors: { '/output/orderNumber': ['Must be text.'] },
        }),
      },
    })

    renderPage()

    await screen.findByTestId('inspector-instance')
    await userEvent.click(screen.getByTestId(`inspector-resolve-${resolvableCallId}`))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('Reason'), { target: { value: 'Try' } })
    await userEvent.click(within(dialog).getByLabelText('Succeeded'))
    fireEvent.change(within(dialog).getByLabelText('orderNumber (text)'), { target: { value: '12' } })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    const field = formItem(within(dialog).getByLabelText('orderNumber (text)'))
    expect(await within(field).findByText('Must be text.')).toBeInTheDocument()
    expect(within(dialog).getByTestId('inspector-command-error')).toHaveTextContent('The request has errors.')
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('shows the Resolve button only on a call the server marks resolvable', async () => {
    stubApi({ detail: { status: 200, body: detailWithResolvable() } })

    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.getAllByTestId(/^inspector-resolve-/).map((button) => button.getAttribute('data-testid'))).toEqual([
      `inspector-resolve-${resolvableCallId}`,
    ])
    expect(screen.getByTestId(`inspector-resolve-${resolvableCallId}`)).toHaveTextContent('Resolve')
  })

  it('shows no Resolve button when no call is resolvable', async () => {
    stubApi()

    renderPage()

    await screen.findByTestId('inspector-instance')
    expect(screen.queryByTestId(/^inspector-resolve-/)).not.toBeInTheDocument()
  })
})
