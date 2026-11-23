import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type {
  ConsoleDeadLetterPage,
  ConsoleFailedWorkPage,
  ConsoleSchedulePage,
  ConsoleTenant,
  ConsoleWebhookDelivery,
  ConsoleWebhookDeliveryPage,
} from '../platform/console'
import { TextProvider } from '../platform/TextProvider'
import { optionTexts } from '../test/antd'
import { expectTitleAloneInBand } from '../test/pageBand'
import { clickInDialog, dialog, pick } from '../test/tableFilter'
import { PlatformContext, type Platform } from './context'
import { ConsolePage } from './ConsolePage'

const platform: Platform = {
  site: {
    name: 'platform',
    titleKey: 'shell.title',
    locales: { default: 'en', fallback: 'en', available: ['en'] },
    navigation: [],
  },
  fallbackTexts: platformEnglish,
  current: { locale: 'en', texts: platformEnglish },
  setCurrent: () => {},
  development: false,
}

const tenant: ConsoleTenant = {
  id: 'default',
  status: 'active',
  applications: [
    {
      id: 'a1',
      name: 'Leave',
      releaseId: 'r1',
      releaseNumber: 3,
      releaseLabel: 'Spring',
      activatedAt: '2026-10-28T09:00:00Z',
    },
    {
      id: 'a2',
      name: 'Purchasing',
      releaseId: 'r2',
      releaseNumber: null,
      releaseLabel: null,
      activatedAt: '2026-10-29T09:00:00Z',
    },
  ],
}

function delivery(overrides: Partial<ConsoleWebhookDelivery>): ConsoleWebhookDelivery {
  return {
    id: 'd1',
    applicationId: 'a1',
    application: 'Leave',
    webhookId: 'w1',
    webhook: 'LeaveHook',
    eventId: 'e1',
    event: 'LeaveApproved',
    state: 'delivered',
    attempts: 1,
    statusCode: 200,
    createdAt: '2026-10-30T09:00:00Z',
    deliveredAt: '2026-10-30T09:00:01Z',
    ...overrides,
  }
}

const webhooks = [
  { id: 'w1', name: 'LeaveHook', applicationId: 'a1', application: 'Leave' },
  { id: 'w2', name: 'OrderHook', applicationId: 'a2', application: 'Purchasing' },
]

const leaveDelivery = delivery({})
const orderDelivery = delivery({
  id: 'd2',
  applicationId: 'a2',
  application: 'Purchasing',
  webhookId: 'w2',
  webhook: 'OrderHook',
  event: 'OrderPlaced',
  state: 'deadLettered',
  attempts: 5,
  statusCode: 503,
  deliveredAt: null,
})

function deliveryPage(items: ConsoleWebhookDelivery[]): ConsoleWebhookDeliveryPage {
  return { items, page: 1, pageSize: 20, totalCount: items.length, webhooks }
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

const failedWork: ConsoleFailedWorkPage = {
  items: [
    {
      kind: 'connector',
      applicationId: 'a1',
      application: 'Leave',
      process: 'Approve',
      instanceId: 'i-1',
      step: 'send',
      callId: 'c-1',
      connector: 'Payroll',
      package: null,
      operation: 'createRecord',
      failedAt: '2026-10-30T09:00:00Z',
      error: 'Timed out.',
    },
    {
      kind: 'instance',
      applicationId: 'a2',
      application: 'Purchasing',
      process: null,
      instanceId: 'i-2',
      step: 'review',
      callId: null,
      connector: null,
      package: null,
      operation: null,
      failedAt: '2026-10-29T09:00:00Z',
      error: null,
    },
  ],
  page: 1,
  pageSize: 20,
  totalCount: 45,
  processes: [
    { id: 'p1', name: 'Approve', applicationId: 'a1', application: 'Leave' },
    { id: 'p2', name: 'Review', applicationId: 'a2', application: 'Purchasing' },
  ],
}

const deliveriesPath = '/api/console/webhook-deliveries'

const deadLettersPath = '/api/console/dead-letters'

const emptyDeadLetters: ConsoleDeadLetterPage = { items: [], page: 1, pageSize: 20, totalCount: 0 }

function deadLetter(id: string, applicationId: string, kind: string, processId: string | null, state = 'open') {
  return {
    id,
    kind,
    applicationId,
    processId,
    eventId: 'e-' + id,
    triggerId: kind === 'webhook.deliver' ? null : 't-' + id,
    processInstanceId: null,
    emailId: null,
    webhookId: kind === 'webhook.deliver' ? 'w-' + id : null,
    occurrenceAt: null,
    attempts: 5,
    lastError: 'Npgsql.PostgresException with SQLSTATE P0001',
    state,
    reason: null,
    settledBy: null,
    createdAt: '2026-10-30T09:00:00Z',
    settledAt: null,
    commands: state === 'open' ? ['redeliver', 'discard'] : [],
  }
}

// A console view waits for its fetch and the table, which is slow while other test files run.
const slow = { timeout: 5000 }

const allLetters: ConsoleDeadLetterPage = {
  items: [
    deadLetter('d1', 'a1', 'event.deliver', 'p1'),
    deadLetter('d2', 'a2', 'webhook.deliver', null),
    deadLetter('d3', 'a1', 'trigger.fanOut', 'p1', 'discarded'),
  ],
  page: 1,
  pageSize: 20,
  totalCount: 3,
}

const schedulesPath = '/api/console/schedules'

const emptySchedules: ConsoleSchedulePage = { items: [], page: 1, pageSize: 20, totalCount: 0 }

/**
 * Answers the dead letter routes with `letters`, the schedules route with `schedules`, the webhook deliveries route with
 * the two deliveries with the fields of `deliveries` over them, the publishing route with an empty answer, and every other
 * route with `answer`, and records every request.
 */
function stubServer(
  answer: (url: string) => Response,
  letters: (url: string) => Response = () => json(emptyDeadLetters),
  schedules: (url: string) => Response = () => json(emptySchedules),
  deliveries: Partial<ConsoleWebhookDeliveryPage> = {},
) {
  const requests: string[] = []
  const bodies: (string | undefined)[] = []
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    requests.push(`${init?.method ?? 'GET'} ${input}`)
    bodies.push(typeof init?.body === 'string' ? init.body : undefined)
    if (input.startsWith(schedulesPath)) {
      return schedules(input)
    }
    if (input.startsWith(deliveriesPath)) {
      return json({ ...deliveryPage([leaveDelivery, orderDelivery]), ...deliveries })
    }
    if (input.startsWith('/api/console/applications/')) {
      return json({ active: null, releases: [], requests: [], page: 1, pageSize: 20, totalCount: 0 })
    }
    return input.startsWith(deadLettersPath) ? letters(input) : answer(input)
  })
  vi.stubGlobal('fetch', fetchMock)
  return Object.assign(fetchMock, { requests, bodies })
}

function tenantAndFailedWork(url: string) {
  return json(url.startsWith('/api/console/tenant') ? tenant : failedWork)
}

const Location = () => <output data-testid="location">{useLocation().search}</output>

function renderPage(address = '/console') {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter initialEntries={[address]}>
          <ConsolePage />
          <Location />
        </MemoryRouter>
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function failedWorkRequests(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.map(([url]) => url).filter((url) => url.startsWith('/api/console/failed-work'))
}

describe('ConsolePage', () => {
  it('holds the title alone in the band', async () => {
    stubServer(tenantAndFailedWork)
    renderPage()

    await screen.findByTestId('console-tenant')

    expectTitleAloneInBand('Operator console')
  })

  it('shows the status and each application with its active release', async () => {
    const fetchMock = stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()

    const view = await screen.findByTestId('console-tenant')
    expect(fetchMock.mock.calls[0]).toEqual(['/api/console/tenant', expect.anything()])
    expect(within(view).getByText('Active')).toBeVisible()
    expect(within(view).getByText('Active').closest('[data-tone]')).toHaveAttribute('data-tone', 'neutral')
    for (const title of ['Application', 'Live release', 'Activated']) {
      expect(within(view).getByRole('columnheader', { name: title })).toBeVisible()
    }
    const leave = within(view).getByText('Leave').closest('tr')!
    expect(within(leave).getByText('#3 Spring')).toBeVisible()
    const purchasing = within(view).getByText('Purchasing').closest('tr')!
    expect(within(purchasing).getByText('—')).toBeVisible()
    expect(screen.queryByTestId('console-forbidden')).toBeNull()
  })

  it("opens an application and its release in Studio from the tenant's table for a reader who may use Studio", async () => {
    stubServer((url) => (url === '/api/me' ? json({ id: 'op', studio: true }) : tenantAndFailedWork(url)))
    renderPage()

    const view = await screen.findByTestId('console-tenant')
    expect(await within(view).findByRole('link', { name: 'Leave' })).toHaveAttribute('href', '/studio?app=Leave')
    expect(within(view).getByRole('link', { name: '#3 Spring' })).toHaveAttribute(
      'href',
      '/studio?app=Leave&view=release&release=3',
    )
    expect(within(view).getByRole('link', { name: 'Purchasing' })).toHaveAttribute('href', '/studio?app=Purchasing')
    // An application with no release has no release page to open.
    expect(within(within(view).getByText('Purchasing').closest('tr')!).getAllByRole('link')).toHaveLength(1)
  })

  it("shows the tenant's application and release as plain text for a reader who may not use Studio", async () => {
    const fetchMock = stubServer((url) =>
      url === '/api/me' ? json({ id: 'op', studio: false }) : tenantAndFailedWork(url),
    )
    renderPage()

    const view = await screen.findByTestId('console-tenant')
    expect(await within(view).findByText('#3 Spring')).toBeVisible()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/me', expect.anything()))
    expect(within(view).queryByRole('link')).toBeNull()
  })

  it('narrows the tenant applications by a filter in the address, and hides the others', async () => {
    const filter = encodeURIComponent(JSON.stringify({ all: [{ field: 'application', op: 'eq', value: 'Leave' }] }))
    stubServer(tenantAndFailedWork)
    renderPage(`/console?applications.filter=${filter}`)

    const view = await screen.findByTestId('console-tenant')
    expect(await within(view).findByText('Leave')).toBeVisible()
    expect(within(view).queryByText('Purchasing')).not.toBeInTheDocument()
  })

  it('labels a read-only tenant', async () => {
    stubServer((url) => json(url.startsWith('/api/console/tenant') ? { ...tenant, status: 'readOnly' } : failedWork))
    renderPage()

    const view = await screen.findByTestId('console-tenant')
    expect(within(view).getByText('Read-only')).toBeVisible()
    expect(within(view).getByText('Read-only').closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
    expect(within(view).queryByText('Active')).toBeNull()
  })

  it('shows each monitor as a part named by its heading, and no card', async () => {
    stubServer(tenantAndFailedWork)
    renderPage()

    const monitors = [
      ['shell.console.tenant.title', 'console-tenant'],
      ['shell.console.failedWork.title', 'console-failed-work'],
      ['shell.console.webhooks.title', 'console-webhook-deliveries'],
      ['shell.console.publishing.title', 'console-publishing'],
      ['shell.console.deadLetters.title', 'console-dead-letters'],
      ['shell.console.schedules.title', 'console-schedules'],
    ] as const
    for (const [title, testId] of monitors) {
      expect(await screen.findByRole('region', { name: platformEnglish[title] })).toBe(screen.getByTestId(testId))
    }
    expect(document.querySelector('.ant-card')).toBeNull()
  })

  it('shows the forbidden page on a 403', async () => {
    stubServer(() => new Response(null, { status: 403 }))
    renderPage()

    const forbidden = await screen.findByTestId('console-forbidden')
    expect(within(forbidden).getByText('This page is for operators.')).toBeVisible()
    expect(screen.queryByTestId('console-tenant')).toBeNull()
    expect(screen.queryAllByRole('region')).toHaveLength(0)
  })

  it('shows an error when the tenant cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(() => new Response(null, { status: 500 }))
    renderPage()

    expect(await screen.findByTestId('console-error')).toBeVisible()
  })

  it('shows the failed work with each item', async () => {
    const fetchMock = stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()

    const view = await screen.findByTestId('console-failed-work')
    expect(await within(view).findByText('Timed out.')).toBeVisible()
    expect(failedWorkRequests(fetchMock)).toEqual(['/api/console/failed-work'])
    for (const title of ['Application', 'Process', 'Instance', 'Step or call', 'Failed', 'Error']) {
      expect(within(view).getByRole('columnheader', { name: title })).toBeVisible()
    }
    const call = within(view).getByText('Timed out.').closest('tr')!
    expect(within(call).getByText('Leave')).toBeVisible()
    expect(within(call).getByText('Approve')).toBeVisible()
    expect(within(call).getByText('i-1')).toBeVisible()
    expect(within(call).getByText('send: Payroll createRecord')).toBeVisible()
    const instance = within(view).getByText('i-2').closest('tr')!
    expect(within(instance).getByText('Purchasing')).toBeVisible()
    expect(within(instance).getByText('review')).toBeVisible()
    expect(within(instance).getAllByText('—')).toHaveLength(2)
  })

  it('links each entry of the failed work to its instance', async () => {
    stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()

    const view = await screen.findByTestId('console-failed-work')

    expect(await within(view).findByTestId('console-failed-work-open-i-1')).toHaveAttribute(
      'href',
      '/console/instances/i-1',
    )
    expect(within(view).getByTestId('console-failed-work-open-i-2')).toHaveAttribute('href', '/console/instances/i-2')
  })

  it('requests the next page of failed work', async () => {
    const fetchMock = stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()
    const view = await screen.findByTestId('console-failed-work')
    expect(await within(view).findByText('Timed out.')).toBeVisible()

    await userEvent.click(within(view).getByTitle('2'))

    await waitFor(() => expect(failedWorkRequests(fetchMock)).toContain('/api/console/failed-work?page=2'))
    expect(screen.getByTestId('location')).toHaveTextContent('failedWork.page=2')
  })

  it('offers a filter on each column of the failed work', async () => {
    stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()
    const view = await screen.findByTestId('console-failed-work')
    expect(await within(view).findByText('Timed out.')).toBeVisible()

    await userEvent.click(within(view).getByRole('button', { name: 'Filter' }))
    await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Column' }))

    expect(optionTexts()).toEqual(['Application', 'Process', 'Instance', 'Step or call', 'Failed', 'Error'])
  })

  it('requests the failed work with the filter and keeps it in the address', async () => {
    const fetchMock = stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()
    const view = await screen.findByTestId('console-failed-work')
    expect(await within(view).findByText('Timed out.')).toBeVisible()
    await userEvent.click(within(view).getByTitle('2'))
    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('failedWork.page=2'))

    await userEvent.click(within(view).getByRole('button', { name: 'Filter' }))
    await pick('Column', 0, 'Application')
    await pick('Value', 0, 'Purchasing')
    await clickInDialog('Apply')

    const filter = JSON.stringify({ all: [{ field: 'application', op: 'eq', value: 'a2' }] })
    await waitFor(() =>
      expect(
        failedWorkRequests(fetchMock)
          .map((url) => new URL(url, 'http://localhost').searchParams.get('filter'))
          .at(-1),
      ).toBe(filter),
    )
    const location = screen.getByTestId('location')
    expect(new URLSearchParams(location.textContent ?? '').get('failedWork.filter')).toBe(filter)
    expect(location).not.toHaveTextContent('failedWork.page')
  })

  it('lists the webhook deliveries', async () => {
    stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()

    const view = await screen.findByTestId('console-webhook-deliveries')
    for (const title of ['Application', 'Webhook', 'Event', 'State', 'Attempts', 'Status code', 'Delivered']) {
      expect(await within(view).findByRole('columnheader', { name: title })).toBeVisible()
    }
    const leave = (await within(view).findByText('LeaveHook')).closest('tr')!
    expect(within(leave).getByText('Delivered', { selector: '.ant-tag' })).toBeVisible()
    expect(within(leave).getByText('Delivered').closest('[data-tone]')).toHaveAttribute('data-tone', 'success')
    expect(within(leave).getByText('200')).toBeVisible()
    const order = within(view).getByText('OrderHook').closest('tr')!
    expect(within(order).getByText('Dead-lettered')).toBeVisible()
    expect(within(order).getByText('Dead-lettered').closest('[data-tone]')).toHaveAttribute('data-tone', 'danger')
    expect(within(order).getByText('5')).toBeVisible()
    expect(within(order).getByText('503')).toBeVisible()
    expect(within(order).getByText('—')).toBeVisible()
  })

  it('keeps the search, filter, sort and page size of the webhook deliveries in the address and the request', async () => {
    const filter = JSON.stringify({ all: [{ field: 'state', op: 'eq', value: 'deadLettered' }] })
    const address = `/console?${new URLSearchParams({
      'webhookDeliveries.search': 'e-1',
      'webhookDeliveries.filter': filter,
      'webhookDeliveries.sort': '-attempts',
    })}`
    const server = stubServer(tenantAndFailedWork, undefined, undefined, { totalCount: 33 })
    const user = userEvent.setup()
    renderPage(address)

    const view = await screen.findByTestId('console-webhook-deliveries')
    const requested = (path: string) => server.requests.filter((request) => request.startsWith(`GET ${path}`))
    await waitFor(() => expect(requested(deliveriesPath)).toHaveLength(1))
    const first = new URL(requested(deliveriesPath)[0].replace('GET ', ''), 'http://localhost').searchParams
    expect(first.get('search')).toBe('e-1')
    expect(first.get('filter')).toBe(filter)
    expect(first.get('sort')).toBe('-attempts')
    await waitFor(() => expect(requested(deadLettersPath)).toHaveLength(1))
    const letters = new URL(requested(deadLettersPath)[0].replace('GET ', ''), 'http://localhost').searchParams
    expect([...letters.keys()]).toEqual([])
    await waitFor(() => expect(requested(schedulesPath)).toHaveLength(1))
    const schedules = new URL(requested(schedulesPath)[0].replace('GET ', ''), 'http://localhost').searchParams
    expect([...schedules.keys()]).toEqual([])
    expect(await within(view).findByText('1–20 of 33')).toBeVisible()

    await user.click(within(view).getByRole('combobox'))
    await user.click(await screen.findByTitle('50 / page'))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('webhookDeliveries.pageSize=50'))
    await waitFor(() => expect(requested(deliveriesPath).some((request) => request.includes('pageSize=50'))).toBe(true))
    expect(screen.getByTestId('location')).toHaveTextContent('webhookDeliveries.sort=-attempts')
    expect(requested(schedulesPath)).toHaveLength(1)
  })

  it('lists the dead letters of every application, including one with no process', async () => {
    const server = stubServer(tenantAndFailedWork, () => json(allLetters))
    renderPage()

    const view = await screen.findByTestId('console-dead-letters')
    expect(await within(view).findByText('webhook.deliver')).toBeVisible()
    expect(server.requests).toContain(`GET ${deadLettersPath}`)
    for (const title of ['Application', 'Kind', 'Event', 'Trigger or webhook', 'Attempts', 'Last error', 'State']) {
      expect(within(view).getByRole('columnheader', { name: title })).toBeVisible()
    }
    const webhook = within(view).getByText('webhook.deliver').closest('tr')!
    expect(within(webhook).getByText('Purchasing')).toBeVisible()
    expect(within(webhook).getByText('w-d2')).toBeVisible()
    expect(within(webhook).getByText('Open')).toBeVisible()
    expect(within(webhook).getByText('Open').closest('[data-tone]')).toHaveAttribute('data-tone', 'attention')
    expect(within(view).getByTestId('console-dead-letter-redeliver-d2')).toBeVisible()
    expect(within(view).getByTestId('console-dead-letter-redeliver-d2').querySelector('.anticon-send')).not.toBeNull()
    expect(within(view).getByTestId('console-dead-letter-discard-d2').querySelector('.anticon-delete')).not.toBeNull()
    const discarded = within(view).getByText('trigger.fanOut').closest('tr')!
    expect(within(discarded).getByText('Discarded')).toBeVisible()
    expect(within(discarded).getByText('Discarded').closest('[data-tone]')).toHaveAttribute('data-tone', 'muted')
    expect(within(discarded).queryByRole('button')).toBeNull()
  })

  it('keeps the search, filter, sort and page size of the dead letters in the address and the request', async () => {
    const filter = JSON.stringify({ all: [{ field: 'state', op: 'eq', value: 'open' }] })
    const address = `/console?${new URLSearchParams({
      'deadLetters.search': 'timeout',
      'deadLetters.filter': filter,
      'deadLetters.sort': '-attempts',
    })}`
    const server = stubServer(tenantAndFailedWork, () => json({ ...allLetters, totalCount: 33 }))
    const user = userEvent.setup()
    renderPage(address)

    const view = await screen.findByTestId('console-dead-letters')
    const requested = (path: string) => server.requests.filter((request) => request.startsWith(`GET ${path}`))
    await waitFor(() => expect(requested(deadLettersPath)).toHaveLength(1))
    const first = new URL(requested(deadLettersPath)[0].replace('GET ', ''), 'http://localhost').searchParams
    expect(first.get('search')).toBe('timeout')
    expect(first.get('filter')).toBe(filter)
    expect(first.get('sort')).toBe('-attempts')
    await waitFor(() => expect(requested(schedulesPath)).toHaveLength(1))
    const schedules = new URL(requested(schedulesPath)[0].replace('GET ', ''), 'http://localhost').searchParams
    expect([...schedules.keys()]).toEqual([])
    expect(await within(view).findByText('1–20 of 33')).toBeVisible()

    await user.click(within(view).getByRole('combobox'))
    await user.click(await screen.findByTitle('50 / page'))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('deadLetters.pageSize=50'))
    await waitFor(() =>
      expect(requested(deadLettersPath).some((request) => request.includes('pageSize=50'))).toBe(true),
    )
    expect(screen.getByTestId('location')).toHaveTextContent('deadLetters.sort=-attempts')
    expect(requested(schedulesPath)).toHaveLength(1)
  })

  it.each([
    ['redeliver', 'Redeliver'],
    ['discard', 'Discard'],
  ])('asks for a reason before it sends %s', async (command, title) => {
    const server = stubServer(tenantAndFailedWork, () => json(allLetters))
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByTestId(`console-dead-letter-${command}-d2`, {}, slow))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(title, { selector: '.ant-modal-title' })).toBeInTheDocument()
    expect(server.requests.filter((request) => request.startsWith('POST'))).toEqual([])

    await user.click(within(dialog).getByRole('button', { name: 'Send' }))
    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument()
    expect(server.requests.filter((request) => request.startsWith('POST'))).toEqual([])

    await user.type(within(dialog).getByRole('textbox', { name: 'Reason' }), 'The receiver is back')
    await user.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const post = server.requests.indexOf(`POST ${deadLettersPath}/d2/${command}`)
    expect(post).toBeGreaterThan(0)
    expect(server.bodies[post]).toBe('{"reason":"The receiver is back"}')
    await waitFor(() => expect(server.requests.slice(post + 1)).toContain(`GET ${deadLettersPath}`))
  })

  it('shows an error in the card when the dead letters cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(tenantAndFailedWork, () => new Response(null, { status: 500 }))
    renderPage()

    expect(await screen.findByTestId('console-dead-letters-error', {}, slow)).toBeVisible()
    expect(screen.getByTestId('console-tenant')).toBeVisible()
  })

  it('lists the schedule triggers with their occurrences and a dash for one that never ran', async () => {
    const schedules: ConsoleSchedulePage = {
      page: 1,
      pageSize: 20,
      totalCount: 2,
      items: [
        {
          applicationId: 'a1',
          application: 'Leave',
          triggerId: 't1',
          trigger: 'followups/RemindFollowups',
          process: 'followups/Remind',
          schedule: '0 6 * * *',
          timeZone: 'Asia/Ho_Chi_Minh',
          lastOccurrenceAt: '2026-10-30T23:00:00Z',
          nextOccurrenceAt: '2026-10-31T23:00:00Z',
        },
        {
          applicationId: 'a2',
          application: 'Purchasing',
          triggerId: 't2',
          trigger: 'followups/RemindFollowups',
          process: 'followups/Remind',
          schedule: '0 6 * * *',
          timeZone: 'Europe/Berlin',
          lastOccurrenceAt: null,
          nextOccurrenceAt: '2026-11-01T05:00:00Z',
        },
      ],
    }
    stubServer(tenantAndFailedWork, undefined, () => json(schedules))
    renderPage()

    const view = await screen.findByTestId('console-schedules')
    await within(view).findByText('Asia/Ho_Chi_Minh', { exact: false })
    for (const title of ['Application', 'Trigger', 'Process', 'Schedule', 'Last occurrence', 'Next occurrence']) {
      expect(within(view).getByRole('columnheader', { name: title })).toBeVisible()
    }
    const first = within(view).getByText('0 6 * * * Asia/Ho_Chi_Minh').closest('tr')!
    expect(within(first).getByText('Leave')).toBeVisible()
    expect(within(first).getByText('followups/RemindFollowups')).toBeVisible()
    expect(within(first).getByText(new Date('2026-10-30T23:00:00Z').toLocaleString('en'))).toBeVisible()
    expect(within(first).getByText(new Date('2026-10-31T23:00:00Z').toLocaleString('en'))).toBeVisible()
    const second = within(view).getByText('0 6 * * * Europe/Berlin').closest('tr')!
    expect(within(second).getByText('Purchasing')).toBeVisible()
    expect(within(second).getByText('—')).toBeVisible()
    expect(within(second).getByText(new Date('2026-11-01T05:00:00Z').toLocaleString('en'))).toBeVisible()
  })

  it('keeps the search, filter, sort and page size of the schedules in the address and the request', async () => {
    const filter = JSON.stringify({ all: [{ field: 'application', op: 'eq', value: 'Leave' }] })
    const address = `/console?${new URLSearchParams({ search: 'Leave', filter, sort: '-nextOccurrenceAt' })}`
    const server = stubServer(tenantAndFailedWork, undefined, () =>
      json({ ...emptySchedules, totalCount: 33 } satisfies ConsoleSchedulePage),
    )
    const user = userEvent.setup()
    renderPage(address)

    const view = await screen.findByTestId('console-schedules')
    const requested = () => server.requests.filter((request) => request.startsWith(`GET ${schedulesPath}`))
    await waitFor(() => expect(requested()).toHaveLength(1))
    const first = new URL(requested()[0].replace('GET ', ''), 'http://localhost').searchParams
    expect(first.get('search')).toBe('Leave')
    expect(first.get('filter')).toBe(filter)
    expect(first.get('sort')).toBe('-nextOccurrenceAt')
    expect(await within(view).findByText('1–20 of 33')).toBeVisible()

    await user.click(within(view).getByRole('combobox'))
    await user.click(await screen.findByTitle('50 / page'))

    await waitFor(() => expect(screen.getByTestId('location')).toHaveTextContent('pageSize=50'))
    await waitFor(() => expect(requested().some((request) => request.includes('pageSize=50'))).toBe(true))
  })

  it('shows an error in the card when the schedules cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(tenantAndFailedWork, undefined, () => new Response(null, { status: 500 }))
    renderPage()

    expect(await screen.findByTestId('console-schedules-error', {}, slow)).toBeVisible()
    expect(screen.getByTestId('console-tenant')).toBeVisible()
  })
})
