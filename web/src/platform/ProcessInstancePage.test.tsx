import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
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
      "inputHidden": true
    },
    {
      "id": "0192f5c3-4e5f-7a60-9b1c-3d4e5f6a7b8d",
      "kind": "package",
      "step": "registerPurchase",
      "package": "PaymentReferences",
      "version": "1.0.0",
      "operation": "reference",
      "state": "succeeded",
      "attempts": [],
      "input": { "title": "Laptops", "amount": 1250.50, "lines": ["a", "b"] },
      "inputHidden": false
    }
  ]
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

function stubApi({
  detail = { status: 200, body: detailText },
  releaseTexts = { status: 200, body: JSON.stringify({ locale: 'en', texts: {} }) },
}: { detail?: Answer; releaseTexts?: Answer } = {}) {
  const fetchMock = vi.fn(async (url: string) => {
    if (url === `${instancesPath}/${instanceId}`) {
      return new Response(detail.body ?? '', { status: detail.status })
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
})
