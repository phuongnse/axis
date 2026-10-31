import { apiFetch } from './api'
import { fetchDeadLetters, forbidden, type DeadLetterItem, type DeadLetterPage } from './inspection'

/** The path of `GET /api/console/instances/{id}`, which answers an instance in the shape of the process inspector's. */
export const consoleInstancesPath = '/api/console/instances'

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

/** One failed instance or call with an unknown outcome, as served by `GET /api/console/failed-work`. */
export interface ConsoleFailedWorkItem {
  kind: 'instance' | 'connector' | 'package'
  applicationId: string
  application: string | null
  process: string | null
  instanceId: string
  step: string
  callId: string | null
  connector: string | null
  package: string | null
  operation: string | null
  /** When the work failed, as an ISO 8601 time. */
  failedAt: string
  error: string | null
}

/** One page of failed work, newest first. */
export interface ConsoleFailedWorkPage {
  items: ConsoleFailedWorkItem[]
  page: number
  pageSize: number
  totalCount: number
}

/** The answer of the failed work route: a page, or the refusal that the page shows. */
export type ConsoleFailedWorkResult = { kind: 'ok'; page: ConsoleFailedWorkPage } | { kind: 'forbidden' }

/** Loads a page of the tenant's failed work, of one application when `application` is set. */
export async function fetchConsoleFailedWork(
  query: { application?: string; page: number; pageSize: number },
  signal?: AbortSignal,
): Promise<ConsoleFailedWorkResult> {
  const parameters = new URLSearchParams({ page: String(query.page), pageSize: String(query.pageSize) })
  if (query.application) {
    parameters.set('application', query.application)
  }
  const response = await apiFetch(`/api/console/failed-work?${parameters}`, { signal })
  if (response.status === 403) {
    return { kind: 'forbidden' }
  }
  if (!response.ok) {
    throw new Error(`Loading the console failed work failed with status ${response.status}.`)
  }
  return { kind: 'ok', page: (await response.json()) as ConsoleFailedWorkPage }
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

/** The path of the console's dead letter routes. A command goes to `<path>/<id>/<command>`. */
export const consoleDeadLettersPath = '/api/console/dead-letters'

/**
 * A dead letter of the tenant, as served by `GET /api/console/dead-letters`: the dead letter of a process with the
 * application it belongs to and its process, which is `null` for a webhook delivery that no instance raised.
 */
export interface ConsoleDeadLetterItem extends DeadLetterItem {
  applicationId: string
  processId: string | null
}

/** One page of the dead letters of the tenant. */
export interface ConsoleDeadLetterPage extends Omit<DeadLetterPage, 'items'> {
  items: ConsoleDeadLetterItem[]
}

/**
 * Loads one page of the dead letters of the tenant, newest first. `query` holds the console API's `application`, `state`,
 * `page` and `pageSize`. A 403 returns {@link forbidden}. A 401 shows the sign-in prompt and throws, as any other failure does.
 */
export async function fetchConsoleDeadLetters(
  query: string,
  signal?: AbortSignal,
): Promise<ConsoleDeadLetterPage | typeof forbidden> {
  return (await fetchDeadLetters(consoleDeadLettersPath, query, signal)) as ConsoleDeadLetterPage | typeof forbidden
}

/**
 * A schedule trigger of an active release, as served by `GET /api/console/schedules`. The times are ISO strings, and
 * `lastOccurrenceAt` is `null` while no occurrence has run.
 */
export interface ConsoleScheduleItem {
  applicationId: string
  application: string
  triggerId: string
  trigger: string
  process: string
  schedule: string
  timeZone: string
  lastOccurrenceAt: string | null
  nextOccurrenceAt: string
}

/** The schedule triggers of the tenant, ordered by application and trigger name. They are not paged. */
export interface ConsoleScheduleList {
  items: ConsoleScheduleItem[]
}

/** The answer of the schedules route: the list, or the refusal that the view shows. */
export type ConsoleSchedulesResult = { kind: 'ok'; schedules: ConsoleScheduleList } | { kind: 'forbidden' }

/** Loads the schedule triggers of the tenant, of one application when `application` is set. */
export async function fetchConsoleSchedules(
  application: string | undefined,
  signal?: AbortSignal,
): Promise<ConsoleSchedulesResult> {
  const query = application ? `?${new URLSearchParams({ application })}` : ''
  const response = await apiFetch(`/api/console/schedules${query}`, { signal })
  if (response.status === 403) {
    return { kind: 'forbidden' }
  }
  if (!response.ok) {
    throw new Error(`Loading the console schedules failed with status ${response.status}.`)
  }
  return { kind: 'ok', schedules: (await response.json()) as ConsoleScheduleList }
}
