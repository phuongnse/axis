import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { ConsoleDeadLetterPage, ConsoleTenant } from '../platform/console'
import { TextProvider } from '../platform/TextProvider'
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

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

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

/** Answers the tenant route with `answer` and the dead letter routes with `letters`, and records every request. */
function stubServer(answer: () => Response, letters: () => Response = () => json(emptyDeadLetters)) {
  const requests: string[] = []
  const bodies: (string | undefined)[] = []
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    requests.push(`${init?.method ?? 'GET'} ${input}`)
    bodies.push(typeof init?.body === 'string' ? init.body : undefined)
    return input.startsWith(deadLettersPath) ? letters() : answer()
  })
  vi.stubGlobal('fetch', fetchMock)
  return Object.assign(fetchMock, { requests, bodies })
}

function renderPage() {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter>
          <ConsolePage />
        </MemoryRouter>
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('ConsolePage', () => {
  it('shows the status and each application with its active release', async () => {
    const fetchMock = stubServer(() => json(tenant))
    renderPage()

    const view = await screen.findByTestId('console-tenant')
    expect(fetchMock.mock.calls[0]).toEqual(['/api/console/tenant', expect.anything()])
    expect(within(view).getByText('Active')).toBeVisible()
    for (const title of ['Application', 'Active release', 'Activated']) {
      expect(within(view).getByRole('columnheader', { name: title })).toBeVisible()
    }
    const leave = within(view).getByText('Leave').closest('tr')!
    expect(within(leave).getByText('#3 Spring')).toBeVisible()
    const purchasing = within(view).getByText('Purchasing').closest('tr')!
    expect(within(purchasing).getByText('—')).toBeVisible()
    expect(screen.queryByTestId('console-forbidden')).toBeNull()
  })

  it('shows the forbidden page on a 403', async () => {
    stubServer(() => new Response(null, { status: 403 }))
    renderPage()

    const forbidden = await screen.findByTestId('console-forbidden')
    expect(within(forbidden).getByText('This page is for operators.')).toBeVisible()
    expect(screen.queryByTestId('console-tenant')).toBeNull()
  })

  it('shows an error when the tenant cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(() => new Response(null, { status: 500 }))
    renderPage()

    expect(await screen.findByTestId('console-error')).toBeVisible()
  })

  it('lists the dead letters of every application, including one with no process', async () => {
    const server = stubServer(
      () => json(tenant),
      () => json(allLetters),
    )
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
    expect(within(view).getByTestId('console-dead-letter-redeliver-d2')).toBeVisible()
    const discarded = within(view).getByText('trigger.fanOut').closest('tr')!
    expect(within(discarded).getByText('Discarded')).toBeVisible()
    expect(within(discarded).queryByRole('button')).toBeNull()
  })

  it('asks for the dead letters of the chosen application', async () => {
    const server = stubServer(
      () => json(tenant),
      () => json(allLetters),
    )
    const user = userEvent.setup()
    renderPage()

    const view = await screen.findByTestId('console-dead-letters')
    await within(view).findByText('webhook.deliver')
    await user.click(within(view).getByRole('combobox', { name: 'Application' }))
    await user.click(await screen.findByTitle('Purchasing'))

    await waitFor(() => expect(server.requests).toContain(`GET ${deadLettersPath}?application=a2`))
  })

  it.each([
    ['redeliver', 'Redeliver'],
    ['discard', 'Discard'],
  ])('asks for a reason before it sends %s', async (command, title) => {
    const server = stubServer(
      () => json(tenant),
      () => json(allLetters),
    )
    const user = userEvent.setup()
    renderPage()

    await user.click(await screen.findByTestId(`console-dead-letter-${command}-d2`))
    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText(title, { selector: '.ant-modal-title' })).toBeInTheDocument()
    expect(server.requests.filter((request) => request.startsWith('POST'))).toEqual([])

    await user.click(within(dialog).getByRole('button', { name: 'Send' }))
    expect(await within(dialog).findByText('Enter a reason.')).toBeInTheDocument()
    expect(server.requests.filter((request) => request.startsWith('POST'))).toEqual([])

    await user.type(within(dialog).getByLabelText('Reason'), 'The receiver is back')
    await user.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    const post = server.requests.indexOf(`POST ${deadLettersPath}/d2/${command}`)
    expect(post).toBeGreaterThan(0)
    expect(server.bodies[post]).toBe('{"reason":"The receiver is back"}')
    await waitFor(() => expect(server.requests.slice(post + 1)).toContain(`GET ${deadLettersPath}`))
  })

  it('shows an error in the card when the dead letters cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(
      () => json(tenant),
      () => new Response(null, { status: 500 }),
    )
    renderPage()

    expect(await screen.findByTestId('console-dead-letters-error')).toBeVisible()
    expect(screen.getByTestId('console-tenant')).toBeVisible()
  })
})
