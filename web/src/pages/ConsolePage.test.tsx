import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { ConsoleTenant } from '../platform/console'
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

function stubServer(answer: () => Response) {
  const fetchMock = vi.fn(async () => answer())
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
})
