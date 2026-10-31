import { apiFetch } from './api'

/** One application of the tenant with its active release, as served by `GET /api/console/tenant`. */
export interface ConsoleApplication {
  id: string
  name: string
  releaseId: string
  /** The number of the release in the application's release history, or `null` when the history has none. */
  releaseNumber: number | null
  releaseLabel: string | null
  /** When the release was activated, as an ISO 8601 time. */
  activatedAt: string
}

/** The tenant of the host, as served by `GET /api/console/tenant`. */
export interface ConsoleTenant {
  id: string
  status: 'active'
  applications: ConsoleApplication[]
}

/** The answer of the tenant route: the tenant, or the refusal that the page shows. */
export type ConsoleTenantResult = { kind: 'ok'; tenant: ConsoleTenant } | { kind: 'forbidden' }

/** Loads the tenant of the host. A 401 shows the sign-in prompt. */
export async function fetchConsoleTenant(signal?: AbortSignal): Promise<ConsoleTenantResult> {
  const response = await apiFetch('/api/console/tenant', { signal })
  if (response.status === 403) {
    return { kind: 'forbidden' }
  }
  if (!response.ok) {
    throw new Error(`Loading the console tenant failed with status ${response.status}.`)
  }
  return { kind: 'ok', tenant: (await response.json()) as ConsoleTenant }
}

/** One webhook delivery, as served by `GET /api/console/webhook-deliveries`. It never holds a body, a URL or a secret. */
export interface ConsoleWebhookDelivery {
  id: string
  applicationId: string
  /** The names come from the release the delivery was staged with, and are `null` when that release no longer holds the webhook. */
  application: string | null
  webhookId: string
  webhook: string | null
  eventId: string
  event: string | null
  state: 'delivered' | 'pending' | 'deadLettered' | 'discarded'
  attempts: number
  statusCode: number | null
  createdAt: string
  deliveredAt: string | null
}

/** A webhook of an active release, for the filter of the webhook deliveries. */
export interface ConsoleWebhook {
  id: string
  name: string
  applicationId: string
  application: string
}

/** One page of webhook deliveries, newest first, with the webhooks of every active release. */
export interface ConsoleWebhookDeliveryPage {
  items: ConsoleWebhookDelivery[]
  page: number
  pageSize: number
  totalCount: number
  webhooks: ConsoleWebhook[]
}

/** The answer of the webhook deliveries route: a page, or the refusal that the page shows. */
export type ConsoleWebhookDeliveriesResult = { kind: 'ok'; page: ConsoleWebhookDeliveryPage } | { kind: 'forbidden' }

/** Loads one page of webhook deliveries. `query` holds the `page`, `application` and `webhook` parameters. */
export async function fetchConsoleWebhookDeliveries(
  query: string,
  signal?: AbortSignal,
): Promise<ConsoleWebhookDeliveriesResult> {
  const response = await apiFetch(`/api/console/webhook-deliveries?${query}`, { signal })
  if (response.status === 403) {
    return { kind: 'forbidden' }
  }
  if (!response.ok) {
    throw new Error(`Loading the console webhook deliveries failed with status ${response.status}.`)
  }
  return { kind: 'ok', page: (await response.json()) as ConsoleWebhookDeliveryPage }
}
