import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { tabTo } from '../test/antd'
import { clickInDialog, pick } from '../test/tableFilter'
import { designTokensFor } from './designTokens'
import { isSignInNeeded } from './signInNeeded'
import { assignLocation } from './navigation'
import { ProcessInspectorWidget } from './ProcessInspectorWidget'
import type { WidgetMetadata } from './site'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'

vi.mock('./navigation')

const instancesPath = '/api/apps/E2eApp/processes/NoteReview/instances'
const deadLettersPath = '/api/apps/E2eApp/processes/NoteReview/dead-letters'

const inspectorWidget: WidgetMetadata = {
  type: 'processInspector',
  entity: null,
  dataSource: null,
  form: null,
  tasksPath: null,
  process: 'NoteReview',
  instancesPath,
  deadLettersPath,
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

const waitingId = '0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b'
const completedId = '0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7c'
const waitingSubjectId = '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6b'
const completedSubjectId = '0192f4a7-3b1c-7e2d-9a4f-1c2d3e4f5a6d'

// In the inspection API's order: newest first.
const instancePage = JSON.stringify({
  items: [
    {
      id: waitingId,
      subjectId: waitingSubjectId,
      subjectLabel: 'Replace the worn carpet tiles',
      state: 'waiting',
      step: 'managerApproval',
      releaseId: '0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01',
      releaseNumber: 1,
      createdAt: '2026-10-10T02:05:00.123456Z',
      endedAt: null,
    },
    {
      id: completedId,
      subjectId: completedSubjectId,
      subjectLabel: null,
      state: 'completed',
      step: 'done',
      releaseId: '0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01',
      releaseNumber: 1,
      createdAt: '2026-10-09T02:05:00Z',
      endedAt: '2026-10-09T03:05:00Z',
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
})

const catalogs = [{ texts: platformEnglish, fallbackTexts: platformEnglish }]

const retryableId = '0192f4b0-1a2b-7c3d-8e4f-5a6b7c8d9e01'
const settledId = '0192f4b0-1a2b-7c3d-8e4f-5a6b7c8d9e02'

// Two open dead letters. The user may take commands on the first only.
const deadLetterPage = JSON.stringify({
  items: [
    {
      id: retryableId,
      kind: 'event.deliver',
      eventId: '0192f4b0-1a2b-7c3d-8e4f-5a6b7c8d9e10',
      triggerId: null,
      processInstanceId: null,
      emailId: null,
      webhookId: null,
      occurrenceAt: null,
      attempts: 5,
      lastError: 'System.InvalidOperationException',
      state: 'open',
      reason: null,
      settledBy: null,
      createdAt: '2026-10-10T02:05:00Z',
      settledAt: null,
      commands: ['redeliver', 'discard'],
    },
    {
      id: settledId,
      kind: 'trigger.fanOut',
      eventId: null,
      triggerId: null,
      processInstanceId: null,
      emailId: null,
      webhookId: null,
      occurrenceAt: null,
      attempts: 3,
      lastError: 'Npgsql.NpgsqlException',
      state: 'open',
      reason: null,
      settledBy: null,
      createdAt: '2026-10-09T02:05:00Z',
      settledAt: null,
      commands: [],
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 2,
})

/**
 * Stubs `fetch` with the instance list and the dead letter list. It returns the requested
 * instance list URLs, and `deadLetterRequests` has the requests of the dead letter list.
 */
function stubInstances(body = instancePage, status = 200, deadLetters = deadLetterPage) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) =>
    input.startsWith(deadLettersPath)
      ? new Response(init?.method === 'POST' ? '{}' : deadLetters, { status: 200 })
      : new Response(body, { status }),
  )
  vi.stubGlobal('fetch', fetchMock)
  const deadLetterRequests = () =>
    fetchMock.mock.calls
      .filter(([url]) => url.startsWith(deadLettersPath))
      .map(([url, init]) => ({ url, method: init?.method ?? 'GET', body: init?.body }))
  return Object.assign(() => fetchMock.mock.calls.map(([url]) => url).filter((url) => url.startsWith(instancesPath)), {
    deadLetterRequests,
  })
}

function CurrentSearch() {
  return <output data-testid="search">{useLocation().search}</output>
}

function renderWidget(search = '') {
  return render(
    <MemoryRouter initialEntries={[`/e2e/inspector${search}`]}>
      <TextProvider catalogs={catalogs} development={false}>
        <ProcessInspectorWidget sitePath="e2e" pageName="Inspector" widget={inspectorWidget} locale="en" />
      </TextProvider>
      <CurrentSearch />
    </MemoryRouter>,
  )
}

function renderWidgetInDark() {
  return render(
    <ThemeModeProvider initialMode="dark">
      <MemoryRouter initialEntries={['/e2e/inspector']}>
        <TextProvider catalogs={catalogs} development={false}>
          <ProcessInspectorWidget sitePath="e2e" pageName="Inspector" widget={inspectorWidget} locale="en" />
        </TextProvider>
      </MemoryRouter>
    </ThemeModeProvider>,
  )
}

describe('ProcessInspectorWidget', () => {
  it('lists the instances in the order of the inspection API, each named by its record and linking to its address', async () => {
    stubInstances()

    renderWidget()

    await screen.findByRole('row', { name: /managerApproval/ })
    for (const title of ['Record', 'State', 'Step', 'Release', 'Started', 'Ended']) {
      expect(screen.getByRole('columnheader', { name: title })).toBeInTheDocument()
    }
    expect(screen.queryByRole('columnheader', { name: 'Open' })).not.toBeInTheDocument()
    const [waiting, completed] = screen.getAllByRole('row').slice(1)
    expect(within(waiting).getByText('Waiting')).toBeInTheDocument()
    expect(within(waiting).getByRole('link', { name: 'Replace the worn carpet tiles' })).toHaveAttribute(
      'href',
      `/e2e/inspector/${waitingId}`,
    )
    expect(waiting).not.toHaveTextContent(waitingSubjectId)
    expect(within(waiting).getAllByRole('cell')[3]).toHaveTextContent(/^1$/)
    // An instance that has not ended shows a muted dash in its end cell.
    expect(within(waiting).getAllByRole('cell')[5]).toHaveTextContent('—')
    expect(within(within(waiting).getAllByRole('cell')[5]).getByText('—')).toHaveStyle({
      color: designTokensFor('light').colour.textMuted,
    })
    expect(within(completed).getByText('Completed')).toBeInTheDocument()
    expect(within(completed).getByText('done')).toBeInTheDocument()
    expect(within(completed).getByRole('link', { name: 'Open' })).toHaveAttribute(
      'href',
      `/e2e/inspector/${completedId}`,
    )
    expect(completed).not.toHaveTextContent(completedSubjectId)
    expect(within(completed).getAllByRole('cell')[5]).not.toHaveTextContent('—')
  })

  it('shows "No subject" as the record link of an instance without a subject', async () => {
    stubInstances(
      JSON.stringify({
        items: [{ ...JSON.parse(instancePage).items[0], subjectId: null, subjectLabel: null }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      }),
    )

    renderWidget()

    const row = await screen.findByRole('row', { name: /managerApproval/ })
    expect(within(row).getByRole('link', { name: 'No subject' })).toHaveAttribute('href', `/e2e/inspector/${waitingId}`)
  })

  it('shows a dash in the release cell when the release has no number', async () => {
    stubInstances(
      JSON.stringify({
        items: [{ ...JSON.parse(instancePage).items[0], releaseNumber: null }],
        page: 1,
        pageSize: 20,
        totalCount: 1,
      }),
    )

    renderWidget()

    const row = await screen.findByRole('row', { name: /managerApproval/ })
    expect(within(row).getAllByRole('cell')[3]).toHaveTextContent('—')
    expect(row).not.toHaveTextContent('0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01')
  })

  it('filters State is Failed through Filter, keeps the filter in the address and goes back to page 1', async () => {
    const requests = stubInstances()
    const user = userEvent.setup()

    renderWidget('?page=2&pageSize=10')

    await screen.findByRole('row', { name: /managerApproval/ })
    await user.click(screen.getByRole('button', { name: 'Filter' }))
    await pick('Column', 0, 'State')
    await pick('Value', 0, 'Failed')
    await clickInDialog('Apply')

    const filter = JSON.stringify({ all: [{ field: 'state', op: 'eq', value: 'failed' }] })
    await waitFor(() =>
      expect(requests()).toContain(`${instancesPath}?${new URLSearchParams({ filter, pageSize: '10' })}`),
    )
    const address = new URLSearchParams(screen.getByTestId('search').textContent ?? '')
    expect(JSON.parse(address.get('filter')!)).toEqual({ all: [{ field: 'state', op: 'eq', value: 'failed' }] })
    expect(address.has('page')).toBe(false)
    expect(address.get('pageSize')).toBe('10')
  })

  it('searches, filters by Step and sorts by Release, keeps the view in the address and sends it as it is', async () => {
    const requests = stubInstances()
    const user = userEvent.setup()

    renderWidget()

    await screen.findByRole('row', { name: /managerApproval/ })
    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'carpet{Enter}')
    await user.click(screen.getByRole('button', { name: 'Filter' }))
    await pick('Column', 0, 'Step')
    await pick('Operator', 0, 'contains')
    await user.type(within(screen.getByRole('dialog')).getByRole('textbox', { name: 'Value' }), 'manager')
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Apply' }))
    await user.click(screen.getByRole('columnheader', { name: 'Release' }))

    const filter = JSON.stringify({ all: [{ field: 'step', op: 'contains', value: 'manager' }] })
    await waitFor(() =>
      expect(requests()).toContain(
        `${instancesPath}?${new URLSearchParams({ search: 'carpet', filter, sort: 'releaseNumber' })}`,
      ),
    )
    const address = new URLSearchParams(screen.getByTestId('search').textContent ?? '')
    expect(address.get('search')).toBe('carpet')
    expect(JSON.parse(address.get('filter')!)).toEqual({ all: [{ field: 'step', op: 'contains', value: 'manager' }] })
    expect(address.get('sort')).toBe('releaseNumber')
    expect(screen.getByRole('row', { name: /managerApproval/ })).toBeInTheDocument()
  })

  it('hides the Step column through Columns and keeps the choice in the address', async () => {
    stubInstances()
    const user = userEvent.setup()

    renderWidget()

    await screen.findByRole('row', { name: /managerApproval/ })
    await user.click(screen.getByRole('button', { name: 'Columns' }))
    const dialog = screen.getByRole('dialog')
    await user.click(within(dialog).getByRole('checkbox', { name: 'Step' }))
    await user.click(within(dialog).getByRole('button', { name: 'Apply' }))

    expect(screen.queryByRole('columnheader', { name: 'Step' })).not.toBeInTheDocument()
    const address = new URLSearchParams(screen.getByTestId('search').textContent ?? '')
    expect(address.get('columns')).toBe('subjectLabel,state,releaseNumber,createdAt,endedAt')
  })

  it('loads the same instances again on Refresh', async () => {
    const requests = stubInstances()
    const user = userEvent.setup()

    renderWidget()

    await screen.findByRole('row', { name: /managerApproval/ })
    await user.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => expect(requests()).toEqual([instancesPath, instancesPath]))
    expect(await screen.findByRole('row', { name: /managerApproval/ })).toBeInTheDocument()
  })

  it('does not read an old state parameter and lists every instance', async () => {
    const requests = stubInstances()

    renderWidget('?state=waiting')

    await screen.findByRole('row', { name: /managerApproval/ })
    expect(requests()).toEqual([instancesPath])
  })

  it('shows the may-not-inspect message and no table on a 403', async () => {
    stubInstances('', 403)

    renderWidget()

    expect(await screen.findByTestId('inspector-forbidden')).toHaveTextContent('You may not inspect this process.')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('shows a load error on any other failure', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubInstances('', 500)

    renderWidget()

    expect(await screen.findByTestId('inspector-error')).toHaveTextContent('The instances could not be loaded.')
    expect(screen.queryByRole('row', { name: /managerApproval/ })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Filter' })).toBeInTheDocument()
  })

  it('asks to sign in when nobody is signed in', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    vi.mocked(assignLocation).mockReset()
    history.pushState(null, '', '/e2e/inspector')
    stubInstances('', 401)

    renderWidget()

    await waitFor(() => expect(isSignInNeeded()).toBe(true))
    expect(assignLocation).not.toHaveBeenCalled()
  })

  it('shows the empty text when there are no instances', async () => {
    stubInstances(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }))

    renderWidget()

    expect(await screen.findByText('No instances.')).toBeInTheDocument()
  })

  it('lists the open dead letters and shows the buttons only on rows whose commands hold them', async () => {
    const requests = stubInstances()

    renderWidget()

    const row = await screen.findByRole('row', { name: /event\.deliver/ })
    expect(requests.deadLetterRequests()[0]).toEqual({ url: `${deadLettersPath}?state=open`, method: 'GET' })
    expect(screen.getByRole('heading', { name: 'Dead letters' })).toBeInTheDocument()
    for (const title of ['Kind', 'Attempts', 'Last error', 'Dead-lettered']) {
      expect(screen.getByRole('columnheader', { name: title })).toBeInTheDocument()
    }
    expect(within(row).getByText('System.InvalidOperationException')).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Redeliver' })).toBeInTheDocument()
    expect(within(row).getByRole('button', { name: 'Discard' })).toBeInTheDocument()
    const other = screen.getByRole('row', { name: /trigger\.fanOut/ })
    expect(within(other).queryByRole('button')).not.toBeInTheDocument()
    expect(screen.getAllByTestId(/^inspector-dead-letter-/)).toHaveLength(2)
  })

  it('shows the name of each dead letter button as a tooltip and opens its reason dialog', async () => {
    stubInstances()
    const user = userEvent.setup()

    renderWidget()

    const row = await screen.findByRole('row', { name: /event\.deliver/ })
    await user.hover(within(row).getByRole('button', { name: 'Redeliver' }))
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Redeliver')
    await user.click(within(row).getByRole('button', { name: 'Discard' }))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Discard', { selector: '.ant-modal-title' })).toBeInTheDocument()
  })

  it('draws Discard in dangerText and names it on keyboard focus in dark mode', async () => {
    stubInstances()
    renderWidgetInDark()

    const row = await screen.findByRole('row', { name: /event\.deliver/ })
    const discard = within(row).getByRole('button', { name: 'Discard' })
    expect(discard).toHaveStyle({ color: designTokensFor('dark').colour.dangerText })
    await tabTo(discard)

    expect(await screen.findByText('Discard', { selector: '[role="tooltip"]' })).toBeInTheDocument()
  })

  it('shows the empty text when no dead letter is open', async () => {
    stubInstances(instancePage, 200, JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0 }))

    renderWidget()

    expect(await screen.findByText('No open dead letters.')).toBeInTheDocument()
  })

  it('shows an error in the dead letter section only, and keeps the instance list', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = vi.fn(async (input: string) =>
      input.startsWith(deadLettersPath) ? new Response('', { status: 500 }) : new Response(instancePage),
    )
    vi.stubGlobal('fetch', fetchMock)

    renderWidget()

    expect(await screen.findByTestId('inspector-dead-letters-error')).toHaveTextContent(
      'The dead letters could not be loaded.',
    )
    expect(screen.getByRole('row', { name: /managerApproval/ })).toBeInTheDocument()
  })

  it('redelivers with a reason, closes the dialog and requests the dead letters again', async () => {
    const requests = stubInstances()
    const user = userEvent.setup()

    renderWidget()

    await user.click(await screen.findByTestId(`inspector-dead-letter-redeliver-${retryableId}`))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Redeliver', { selector: '.ant-modal-title' })).toBeInTheDocument()
    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'The trigger is back')
    await user.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await waitFor(() =>
      expect(requests.deadLetterRequests().map((request) => `${request.method} ${request.url}`)).toEqual([
        `GET ${deadLettersPath}?state=open`,
        `POST ${deadLettersPath}/${retryableId}/redeliver`,
        `GET ${deadLettersPath}?state=open`,
      ]),
    )
    expect(requests.deadLetterRequests()[1].body).toBe('{"reason":"The trigger is back"}')
  })

  it('goes back to the last page with rows when settling empties the current page', async () => {
    const item = (id: string, kind: string) => ({
      ...JSON.parse(deadLetterPage).items[0],
      id,
      kind,
    })
    const first = item(retryableId, 'event.deliver')
    const second = item(settledId, 'trigger.fanOut')
    let settled = false
    const deadLetterGets: string[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn(async (input: string, init?: RequestInit) => {
        if (!input.startsWith(deadLettersPath)) {
          return new Response(instancePage)
        }
        if (init?.method === 'POST') {
          settled = true
          return new Response('{}')
        }
        deadLetterGets.push(input)
        const onSecondPage = input.includes('page=2')
        const items = settled ? (onSecondPage ? [] : [first]) : onSecondPage ? [second] : [first]
        return new Response(
          JSON.stringify({ items, page: onSecondPage ? 2 : 1, pageSize: 1, totalCount: settled ? 1 : 2 }),
        )
      }),
    )
    const user = userEvent.setup()

    renderWidget()

    await screen.findByRole('row', { name: /event\.deliver/ })
    await user.click(screen.getByTitle('2'))
    await user.click(await screen.findByTestId(`inspector-dead-letter-discard-${settledId}`))
    const dialog = await screen.findByRole('dialog')
    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Not needed')
    await user.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(await screen.findByRole('row', { name: /event\.deliver/ })).toBeInTheDocument()
    expect(screen.queryByText('No open dead letters.')).not.toBeInTheDocument()
    expect(deadLetterGets).toEqual([
      `${deadLettersPath}?state=open`,
      `${deadLettersPath}?state=open&page=2`,
      `${deadLettersPath}?state=open&page=2`,
      `${deadLettersPath}?state=open`,
    ])
  })

  it("shows the server's problem title when a command is refused", async () => {
    const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return new Response(JSON.stringify({ title: 'The dead letter is not open.' }), { status: 409 })
      }
      return new Response(input.startsWith(deadLettersPath) ? deadLetterPage : instancePage)
    })
    vi.stubGlobal('fetch', fetchMock)
    const user = userEvent.setup()

    renderWidget()

    await user.click(await screen.findByTestId(`inspector-dead-letter-discard-${retryableId}`))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Discard', { selector: '.ant-modal-title' })).toBeInTheDocument()
    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'Not needed')
    await user.click(within(dialog).getByRole('button', { name: 'Send' }))

    expect(await within(dialog).findByTestId('inspector-command-error')).toHaveTextContent(
      'The dead letter is not open.',
    )
  })
})
