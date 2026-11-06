import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { PlatformContext, type Platform } from './context'
import { TenantExportSection } from './TenantExportSection'

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

const exportId = '6f1c0d52-8d0e-4a47-9c1b-2f7e5a9d4c01'
const warning = 'The file holds every value in clear, sensitive ones included. Keep it somewhere safe.'
const storageKey = 'axis.tenantExport'

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function tenantExport(state: string) {
  return {
    id: exportId,
    tenantId: 'quiz',
    state,
    createdAt: '2026-11-06T09:00:00Z',
    expiresAt: state === 'succeeded' ? '2026-11-13T09:00:00Z' : null,
  }
}

/** Answers the current user with the flag, a POST with a queued export and a GET with `read`. */
function stubServer(tenantExports: boolean, read: () => Response = () => json(tenantExport('succeeded'))) {
  const fetchMock = vi.fn(async (input: string, init?: RequestInit) => {
    if (input === '/api/me') {
      return json({ id: 'me', displayName: 'Admin', roles: [], users: true, tenantExports })
    }
    return init?.method === 'POST' ? json(tenantExport('queued'), 202) : read()
  })
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function posts(fetchMock: ReturnType<typeof stubServer>) {
  return fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')
}

function renderSection() {
  return render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <TenantExportSection />
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

beforeEach(() => localStorage.clear())

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('TenantExportSection', () => {
  it('renders nothing without the flag', async () => {
    const fetchMock = stubServer(false)
    renderSection()

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/me', expect.anything()))
    expect(screen.queryByTestId('tenant-export')).toBeNull()
    expect(screen.queryByText('Export data')).toBeNull()
  })

  it('warns first, and sends the start only when the confirmation is confirmed', async () => {
    const fetchMock = stubServer(true)
    renderSection()

    const section = await screen.findByTestId('tenant-export')
    expect(within(section).getByRole('heading', { name: 'Export data' })).toBeVisible()
    expect(within(section).getByText(warning)).toBeVisible()

    await userEvent.click(within(section).getByRole('button', { name: 'Start export…' }))
    const dialog = (await screen.findByText('Start the export?')).closest<HTMLElement>('[role="dialog"]')!
    expect(within(dialog).getByText(warning)).toBeInTheDocument()
    expect(posts(fetchMock)).toHaveLength(0)

    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(posts(fetchMock)).toHaveLength(0)

    await userEvent.click(within(section).getByRole('button', { name: 'Start export…' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Start' }))

    await vi.waitFor(() => expect(posts(fetchMock)).toHaveLength(1))
    expect(posts(fetchMock)[0][0]).toBe('/api/tenant/exports')
  })

  it('follows a started export and offers the download once it has succeeded', async () => {
    stubServer(true)
    renderSection()

    await userEvent.click(await screen.findByRole('button', { name: 'Start export…' }))
    await userEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Start' }))

    expect(await screen.findByRole('button', { name: 'Download ZIP' })).toBeVisible()
    expect(screen.getByTestId('tenant-export-ready')).toHaveTextContent('The file is ready until')
    expect(localStorage.getItem(storageKey)).toBe(exportId)
  })

  it('shows a stored export again after a remount and forgets one that is gone', async () => {
    localStorage.setItem(storageKey, exportId)
    const fetchMock = stubServer(true)
    const first = renderSection()

    expect(await screen.findByRole('button', { name: 'Download ZIP' })).toBeVisible()
    expect(fetchMock).toHaveBeenCalledWith(`/api/tenant/exports/${exportId}`, expect.anything())
    expect(screen.queryByRole('button', { name: 'Start export…' })).toBeNull()
    first.unmount()

    stubServer(true, () => new Response(null, { status: 410 }))
    renderSection()

    expect(await screen.findByRole('button', { name: 'Start export…' })).toBeVisible()
    expect(localStorage.getItem(storageKey)).toBeNull()
  })

  it('says a failed export failed and offers to start again', async () => {
    localStorage.setItem(storageKey, exportId)
    stubServer(true, () => json(tenantExport('failed')))
    renderSection()

    expect(await screen.findByText('The export failed. Start it again.')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Start export…' })).toBeVisible()
  })
})
