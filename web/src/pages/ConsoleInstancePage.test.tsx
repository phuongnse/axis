import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
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
  subjectId: null,
  releaseId: '0192f49a-1b2c-7d3e-8f40-5b6c7d8e9f01',
  state: 'failed',
  step: 'managerApproval',
  createdAt: '2026-10-10T02:05:00.123456Z',
  endedAt: '2026-10-10T02:06:00.000000Z',
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
        transitions: [{ next: 'done', on: 'approve' }],
      },
      { name: 'done', type: 'end', labelKey: null, transitions: [] },
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

function renderPage() {
  render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <MemoryRouter initialEntries={[`/console/instances/${instanceId}`]}>
          <Routes>
            <Route path="console/instances/:id" element={<ConsoleInstancePage />} />
          </Routes>
        </MemoryRouter>
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('ConsoleInstancePage', () => {
  it('shows the steps by name with the failed step current, the history error and the hidden call input', async () => {
    const fetchMock = stubServer(
      () => new Response(JSON.stringify(detail), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    renderPage()

    const step = await screen.findByTestId('inspector-step-managerApproval')
    expect(fetchMock.mock.calls[0]).toEqual([`/api/console/instances/${instanceId}`, expect.anything()])
    expect(within(step).getByText('managerApproval')).toBeVisible()
    expect(within(step).getByText('Current')).toBeVisible()
    expect(within(step).getByText('Taken')).toBeVisible()
    expect(screen.queryByText('purchaseRequest.managerApproval')).toBeNull()
    expect(screen.getByText('System.InvalidOperationException')).toBeVisible()
    expect(within(screen.getByTestId('inspector-call-c-1')).getByText('Input: Hidden')).toBeVisible()
    expect(screen.queryByTestId('inspector-command-retry')).toBeNull()
    expect(screen.getByRole('link', { name: 'Operator console' })).toHaveAttribute('href', '/console')
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
