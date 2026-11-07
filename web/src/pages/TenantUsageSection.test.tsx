import { act, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TenantUsageDayTable } from '../platform/TenantUsageDayTable'
import { TextProvider } from '../platform/TextProvider'
import type { TenantUsage } from '../platform/tenantUsage'
import { PlatformContext, type Platform } from './context'
import { TenantUsageSection } from './TenantUsageSection'

vi.mock('../platform/TenantUsageDayTable', async (importOriginal) => {
  const original = await importOriginal<typeof import('../platform/TenantUsageDayTable')>()
  return { TenantUsageDayTable: vi.fn(original.TenantUsageDayTable) }
})

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

const gigabyte = 1024 ** 3
const megabyte = 1024 ** 2

const usage: TenantUsage = {
  quotas: {
    storage: { limit: gigabyte, used: megabyte * 512 },
    users: { limit: 5, used: 5 },
    applications: { limit: 10, used: 3 },
    emailsPerDay: { limit: 100, used: 0 },
    concurrentImports: { limit: 2, used: null },
    concurrentConnectorCalls: { limit: null, used: null },
    concurrentQueries: { limit: 8, used: null },
    workerWeight: { limit: null, used: null },
    authoringRequests: { limit: null, used: 12 },
  },
  days: [
    {
      day: '2026-11-06',
      databaseBytes: megabyte * 3,
      fileBytes: 2048,
      users: 5,
      applications: null,
      emailsSent: 4,
      connectorCalls: 90,
      workItemsRun: 7,
    },
    {
      day: '2026-11-05',
      databaseBytes: megabyte * 3,
      fileBytes: 0,
      users: null,
      applications: 3,
      emailsSent: 0,
      connectorCalls: 0,
      workItemsRun: 0,
    },
  ],
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function stubServer(answer: Response) {
  const fetchMock = vi.fn(async () => answer)
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

function renderSection() {
  return render(
    <PlatformContext.Provider value={platform}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <TenantUsageSection />
      </TextProvider>
    </PlatformContext.Provider>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('TenantUsageSection', () => {
  it('shows each quota with its limit and use', async () => {
    stubServer(json(usage))
    renderSection()

    const section = await screen.findByTestId('tenant-usage')
    expect(within(section).getByRole('heading', { name: 'Usage' })).toBeVisible()
    expect(within(screen.getByTestId('tenant-usage-quota-storage')).getByText('512 MB of 1 GB')).toBeVisible()
    expect(within(screen.getByTestId('tenant-usage-quota-applications')).getByText('3 of 10')).toBeVisible()
    expect(within(screen.getByTestId('tenant-usage-quota-emailsPerDay')).getByText('0 of 100')).toBeVisible()
    expect(within(screen.getByTestId('tenant-usage-quota-workerWeight')).getByText('No limit')).toBeVisible()
    expect(within(screen.getByTestId('tenant-usage-quota-concurrentQueries')).getByText('8')).toBeVisible()
    const authoring = screen.getByTestId('tenant-usage-quota-authoringRequests')
    expect(within(authoring).getByText('No limit')).toBeVisible()
    expect(within(authoring).getByText('12')).toBeVisible()
    expect(within(screen.getByTestId('tenant-usage-quota-concurrentQueries')).queryByRole('progressbar')).toBeNull()
  })

  it('marks a quota at its limit in words and leaves the others unmarked', async () => {
    stubServer(json(usage))
    renderSection()

    const users = await screen.findByTestId('tenant-usage-quota-users')
    expect(within(users).getByText('5 of 5')).toBeVisible()
    expect(within(users).getByText('At limit')).toBeVisible()
    expect(screen.getAllByText('At limit')).toHaveLength(1)
    expect(within(screen.getByTestId('tenant-usage-quota-applications')).queryByText('At limit')).toBeNull()
  })

  it('lists one row per day of the answer', async () => {
    stubServer(json(usage))
    renderSection()

    const days = await screen.findByTestId('tenant-usage-days')
    const rows = within(days).getAllByRole('row')
    expect(rows).toHaveLength(3)
    expect(within(rows[1]).getByText('3 MB')).toBeVisible()
    expect(vi.mocked(TenantUsageDayTable)).toHaveBeenCalledWith(
      expect.objectContaining({ days: usage.days, locale: 'en', testId: 'tenant-usage-days' }),
      undefined,
    )
  })

  it('renders nothing when the route answers 404', async () => {
    const fetchMock = stubServer(new Response(null, { status: 404 }))
    renderSection()

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/tenant/usage', expect.anything()))
    await act(() => new Promise((resolve) => setTimeout(resolve, 0)))
    expect(screen.queryByTestId('tenant-usage')).toBeNull()
    expect(screen.queryByText('Usage')).toBeNull()
  })

  it('renders nothing and logs a warning when the route answers 503', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const fetchMock = stubServer(new Response(null, { status: 503 }))
    renderSection()

    await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledWith('/api/tenant/usage', expect.anything()))
    await vi.waitFor(() => expect(warn).toHaveBeenCalledWith('Reading the usage failed with status 503.'))
    expect(screen.queryByTestId('tenant-usage')).toBeNull()
  })
})
