import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { expectTitleAloneInBand } from '../test/pageBand'
import { ShellAsideHost } from '../test/shellAsideHost'
import { TextProvider } from '../platform/TextProvider'
import { ThemeModeProvider } from '../platform/theme'
import { PlatformContext, type Platform } from './context'
import { ConsoleInstancePage } from './ConsoleInstancePage'

const instanceId = '0192f4a9-2d3e-7f40-8a1b-2c3d4e5f6a7b'

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

// A failed instance at the step "managerApproval", whose task step has a label key that the console cannot translate.
const detail = {
  id: instanceId,
  process: 'PurchaseRequestApproval',
  processTitleKey: 'processes.purchaseRequestApproval.title',
  subjectId: null,
  subjectEntity: null,
  subjectLabel: null,
  releaseId: '0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01',
  releaseNumber: 3,
  state: 'failed',
  step: 'managerApproval',
  createdAt: '2026-10-10T02:05:00.123456Z',
  endedAt: '2026-10-10T02:06:00.000000Z',
  startedBy: { kind: 'person', name: 'Ada Lovelace', id: 'ada', via: 'purchaseRequest.submit' },
  input: null,
  result: null,
  studio: null,
  timerDueAt: null,
  parent: null,
  subProcesses: [],
  graph: {
    start: 'managerApproval',
    steps: [
      {
        name: 'managerApproval',
        type: 'task',
        labelKey: 'purchaseRequest.managerApproval',
        event: null,
        eventLabelKey: null,
        transitions: [{ next: 'done', on: 'approve', branch: null, labelKey: 'task.approve', after: null }],
      },
      { name: 'done', type: 'end', labelKey: null, event: null, eventLabelKey: null, transitions: [] },
    ],
  },
  path: [
    {
      step: 'managerApproval',
      startedAt: '2026-10-10T02:05:00.000000Z',
      finishedAt: '2026-10-10T02:06:00.000000Z',
      decision: null,
      output: null,
      error: 'System.InvalidOperationException',
      problem: null,
      traceId: null,
    },
  ],
  tasks: [],
  calls: [
    {
      id: 'c-1',
      kind: 'connector',
      step: 'managerApproval',
      connector: 'Purchasing',
      operation: 'createOrder',
      state: 'unknown',
      attempts: [],
      input: null,
      inputHidden: true,
      resolvable: false,
    },
  ],
  commands: [],
}

function stubServer(answer: () => Response) {
  const fetchMock = vi.fn(async (_url: string) => answer())
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderPage(search = '') {
  render(
    <PlatformContext.Provider value={platform}>
      <ThemeModeProvider>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <MemoryRouter initialEntries={[`/console/instances/${instanceId}${search}`]}>
            <ShellAsideHost>
              <Routes>
                <Route path="console/instances/:id" element={<ConsoleInstancePage />} />
              </Routes>
            </ShellAsideHost>
          </MemoryRouter>
        </TextProvider>
      </ThemeModeProvider>
    </PlatformContext.Provider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('ConsoleInstancePage', () => {
  it('holds the process title alone in the band', async () => {
    stubServer(
      () => new Response(JSON.stringify(detail), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    renderPage()

    await screen.findByRole('heading', { level: 1, name: 'PurchaseRequestApproval' })

    expectTitleAloneInBand('PurchaseRequestApproval')
  })

  it('draws the steps by name with the failed step current, and the stored error in the banner and not in History', async () => {
    const fetchMock = stubServer(
      () => new Response(JSON.stringify(detail), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    renderPage()

    const step = await screen.findByTestId('rf__node-managerApproval', undefined, { timeout: 5000 })
    expect(fetchMock.mock.calls[0]).toEqual([`/api/console/instances/${instanceId}`, expect.anything()])
    expect(within(step).getByText('managerApproval')).toBeVisible()
    expect(within(step).getByRole('img', { name: 'Failed' })).toHaveAttribute('data-tone', 'danger')
    expect(within(step).getByRole('button', { name: 'managerApproval' })).toHaveAttribute('aria-current', 'true')
    expect(screen.queryByText('purchaseRequest.managerApproval')).toBeNull()
    expect(screen.queryByText('Approve')).toBeNull()

    await userEvent.click(screen.getByRole('tab', { name: 'History' }))

    const history = within(screen.getByTestId('inspector-history'))
    expect(history.getByText('createOrder (Purchasing) has an unknown outcome.')).toBeVisible()
    expect(history.queryByText('System.InvalidOperationException')).toBeNull()
    expect(history.queryByText('Input: Hidden')).toBeNull()
    expect(within(screen.getByTestId('inspector-failure')).getByText(/System.InvalidOperationException/)).toBeVisible()
    expect(screen.queryByTestId('inspector-command-retry')).toBeNull()
  })

  it("opens a step's run in the side panel by the step's name, with the stored error and a sub-process that is not a link", async () => {
    const child = {
      id: '0192f4c0-1a2b-7c3d-8e4f-5a6b7c8d9e02',
      process: 'ChildJob',
      processTitleKey: 'processes.childJob.title',
      step: 'managerApproval',
      state: 'running',
      createdAt: '2026-10-10T02:05:10.000000Z',
      endedAt: null,
    }
    stubServer(
      () =>
        new Response(JSON.stringify({ ...detail, subProcesses: [child] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        }),
    )
    renderPage('?step=managerApproval')

    const panel = within(await screen.findByTestId('inspector-step-run'))
    expect(panel.getByRole('heading', { name: 'managerApproval' })).toBeVisible()
    expect(panel.getByText('Failed').closest('[data-tone]')).toHaveAttribute('data-tone', 'danger')
    expect(panel.getByText('ChildJob')).toBeVisible()
    expect(panel.queryByRole('link', { name: 'ChildJob' })).toBeNull()
    await userEvent.click(panel.getByRole('tab', { name: 'Error' }))
    expect(panel.getByText('System.InvalidOperationException')).toBeVisible()
  })

  it('is headed by the name of its process, with its state as a tag and Process as its line', async () => {
    stubServer(
      () => new Response(JSON.stringify(detail), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    renderPage()

    const heading = within(screen.getByTestId('page-heading'))
    expect(await heading.findByRole('heading', { level: 1, name: 'PurchaseRequestApproval' })).toBeVisible()
    expect(heading.getByText('Process')).toBeVisible()
    expect(heading.getByText('Failed').closest('[data-tone]')).toHaveAttribute('data-tone', 'danger')
  })

  it('shows the instance in parts: its facts, then the tabs, with a rule before every part but the first', async () => {
    stubServer(
      () => new Response(JSON.stringify(detail), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    renderPage('?tab=history')

    const page = await screen.findByTestId('inspector-instance')
    expect(within(page).getByTestId('studio-section-inspector-facts')).toBeVisible()
    expect(within(page).getByTestId('studio-section-inspector-tabs').previousElementSibling).toHaveAttribute(
      'role',
      'separator',
    )
    expect(within(page).getAllByRole('separator')).toHaveLength(1)
    expect(within(page).getByTestId('inspector-history')).toBeVisible()
  })

  it('posts the reason of a retry to the console route and shows the instance read again', async () => {
    const json = (body: unknown) =>
      new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    let reads = 0
    const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => {
      if (init?.method === 'POST') {
        return json({ id: instanceId })
      }
      reads += 1
      return json(reads === 1 ? { ...detail, commands: ['retry'] } : { ...detail, state: 'running', commands: [] })
    })
    vi.stubGlobal('fetch', fetchMock)
    renderPage()

    await userEvent.click(await screen.findByTestId('inspector-command-retry'))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Reason' }), {
      target: { value: 'The manager is set now' },
    })
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(screen.queryByTestId('inspector-command-retry')).toBeNull())
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
    expect(post[0]).toBe(`/api/console/instances/${instanceId}/retry`)
    expect(JSON.parse(post[1]!.body as string)).toEqual({ reason: 'The manager is set now' })
  })

  it('shows a not-found message when the instance does not exist', async () => {
    stubServer(() => new Response(null, { status: 404 }))
    renderPage()

    const message = await screen.findByTestId('console-instance-not-found')
    expect(within(message).getByText('This instance does not exist.')).toBeVisible()
  })

  it('shows the forbidden page on a 403', async () => {
    stubServer(() => new Response(null, { status: 403 }))
    renderPage()

    const forbidden = await screen.findByTestId('console-forbidden')
    expect(within(forbidden).getByText('This page is for operators.')).toBeVisible()
  })

  it('shows an error when the instance cannot be loaded', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    stubServer(() => new Response(null, { status: 500 }))
    renderPage()

    expect(await screen.findByTestId('console-instance-error')).toBeVisible()
  })
})
