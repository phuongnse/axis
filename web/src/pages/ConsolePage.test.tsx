import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import type { ConsoleTenant, ConsoleWebhookDelivery, ConsoleWebhookDeliveryPage } from '../platform/console'
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

/** Answers the webhook deliveries route with `deliveries`, filtered by the `webhook` parameter, and every other route with `answer`. */
function stubServer(answer: () => Response, deliveries = [leaveDelivery, orderDelivery]) {
  const fetchMock = vi.fn(async (url: string) => {
    if (url.startsWith('/api/console/webhook-deliveries')) {
      const webhook = new URL(url, 'http://localhost').searchParams.get('webhook')
      return json(deliveryPage(deliveries.filter((row) => !webhook || row.webhookId === webhook)))
    }
    return answer()
  })
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

  it('lists the webhook deliveries and filters them by webhook', async () => {
    const fetchMock = stubServer(() => json(tenant))
    renderPage()

    const view = await screen.findByTestId('console-webhook-deliveries')
    for (const title of ['Application', 'Webhook', 'Event', 'State', 'Attempts', 'Status code', 'Delivered']) {
      expect(await within(view).findByRole('columnheader', { name: title })).toBeVisible()
    }
    const leave = (await within(view).findByText('LeaveHook')).closest('tr')!
    expect(within(leave).getByText('Delivered', { selector: '.ant-tag' })).toBeVisible()
    expect(within(leave).getByText('200')).toBeVisible()
    const order = within(view).getByText('OrderHook').closest('tr')!
    expect(within(order).getByText('Dead-lettered')).toBeVisible()
    expect(within(order).getByText('5')).toBeVisible()
    expect(within(order).getByText('503')).toBeVisible()
    expect(within(order).getByText('—')).toBeVisible()

    fireEvent.mouseDown(within(view).getByRole('combobox', { name: 'Webhook' }))
    fireEvent.click(await screen.findByText('Purchasing / OrderHook'))

    await waitFor(() => expect(fetchMock.mock.calls.at(-1)?.[0]).toContain('webhook=w2'))
    await waitFor(() => expect(within(view).queryByText('LeaveHook')).toBeNull())
    expect(within(view).getByText('OrderHook')).toBeVisible()
  })
})
