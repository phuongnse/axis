import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { ConsoleFailedWorkPage, ConsoleTenant } from '../platform/console'
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
}

function stubServer(answer: (url: string) => Response) {
  const fetchMock = vi.fn(async (url: string) => answer(url))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
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

function failedWorkRequests(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.map(([url]) => url).filter((url) => url.startsWith('/api/console/failed-work'))
}

describe('ConsolePage', () => {
  it('shows the status and each application with its active release', async () => {
    const fetchMock = stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
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

  it('shows the failed work with each item', async () => {
    const fetchMock = stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()

    const view = await screen.findByTestId('console-failed-work')
    expect(await within(view).findByText('Timed out.')).toBeVisible()
    expect(failedWorkRequests(fetchMock)).toEqual(['/api/console/failed-work?page=1&pageSize=20'])
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

    await waitFor(() => expect(failedWorkRequests(fetchMock)).toContain('/api/console/failed-work?page=2&pageSize=20'))
  })

  it('filters the failed work by application and returns to the first page', async () => {
    const fetchMock = stubServer((url) => json(url.startsWith('/api/console/tenant') ? tenant : failedWork))
    renderPage()
    const view = await screen.findByTestId('console-failed-work')
    expect(await within(view).findByText('Timed out.')).toBeVisible()
    await userEvent.click(within(view).getByTitle('2'))
    await waitFor(() => expect(failedWorkRequests(fetchMock)).toContain('/api/console/failed-work?page=2&pageSize=20'))

    await userEvent.click(within(view).getByRole('combobox'))
    await userEvent.click(await screen.findByTitle('Purchasing'))

    await waitFor(() =>
      expect(failedWorkRequests(fetchMock).at(-1)).toBe('/api/console/failed-work?page=1&pageSize=20&application=a2'),
    )
  })
})
