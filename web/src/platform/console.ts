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
