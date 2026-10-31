import { apiFetch } from './api'
import { fetchDeadLetters, forbidden, type DeadLetterItem, type DeadLetterPage } from './inspection'

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
