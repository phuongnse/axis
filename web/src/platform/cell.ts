import { apiFetch } from './api'

/** The signed-in platform operator, as served by `GET /api/cell/session`. */
export interface CellSession {
  displayName: string
}

/** The newest stored result of the weekly restore check: when the run started, as an ISO 8601 time, its source tenant and whether it passed. */
export interface CellRestoreCheck {
  ranAt: string
  tenantId: string
  passed: boolean
}

/** The cell, as served by `GET /api/cell`. `cap` is `null` when the cell has no cap on its tenants and `restoreCheck` when no check has run. */
export interface Cell {
  id: string
  region: string
  tenants: number
  cap: number | null
  restoreCheck: CellRestoreCheck | null
}

/** One step of a job. `error` is `null` unless the step failed. */
export interface CellJobStep {
  name: string
  state: 'pending' | 'running' | 'succeeded' | 'failed'
  error: string | null
}

/** One job of the cell, as served by `GET /api/cell/jobs`. `error` is `null` unless the job failed. */
export interface CellJob {
  id: string
  kind: string
  tenantId: string
  state: 'queued' | 'running' | 'succeeded' | 'failed'
  /** When the job was created, as an ISO 8601 time. */
  createdAt: string
  steps: CellJobStep[]
  error: string | null
}

export type CellTenantStatus = 'active' | 'readOnly' | 'suspended' | 'provisioning' | 'deleting'

/** The failed work of a tenant, as the worker counted it. */
export interface CellFailedWork {
  instances: number
  unknownCalls: number
  deadLetters: number
}

/** One tenant of the cell, as served by `GET /api/cell/tenants`. `failedWork` is `null` until the worker has counted the tenant. */
export interface CellTenant {
  id: string
  status: CellTenantStatus
  application: string | null
  hosts: string[]
  failedWork: CellFailedWork | null
}

/** One quota of a tenant. `value` is `null` for no limit, and `overridden` is true when the tenant overrides the cell's default. */
export interface CellQuota {
  value: number | null
  overridden: boolean
}

/** One tenant with its quotas, as served by `GET /api/cell/tenants/{id}`. */
export interface CellTenantDetail extends CellTenant {
  quotas: Record<string, CellQuota>
}

/**
 * The answer of a console route: the data, or the refusal that the console shows. `unauthorized` means that the
 * session is missing or has expired, and `forbidden` that the person lacks the operator role.
 */
export type CellResult<T> = { kind: 'ok'; value: T } | { kind: 'unauthorized' } | { kind: 'forbidden' }

/**
 * Reads a console route. A 401 never shows the tenant sign-in prompt, as it starts the wrong realm's sign-in. With
 * `notFoundAsNull`, a 404 answers `null` as the value, for a route that answers 404 for an unknown id.
 */
async function fetchCellRoute<T>(
  url: string,
  name: string,
  signal?: AbortSignal,
  notFoundAsNull = false,
): Promise<CellResult<T>> {
  const response = await apiFetch(url, { signal }, { promptSignInOnUnauthorized: false })
  if (response.status === 401) {
    return { kind: 'unauthorized' }
  }
  if (response.status === 403) {
    return { kind: 'forbidden' }
  }
  if (notFoundAsNull && response.status === 404) {
    return { kind: 'ok', value: null as T }
  }
  if (!response.ok) {
    throw new Error(`Loading the cell ${name} failed with status ${response.status}.`)
  }
  return { kind: 'ok', value: (await response.json()) as T }
}

/** Loads the signed-in operator. */
export function fetchCellSession(signal?: AbortSignal): Promise<CellResult<CellSession>> {
  return fetchCellRoute('/api/cell/session', 'session', signal)
}

/** Loads the cell. */
export function fetchCell(signal?: AbortSignal): Promise<CellResult<Cell>> {
  return fetchCellRoute('/api/cell', 'overview', signal)
}

/** Loads the newest jobs of the cell, newest first, or the jobs of one `tenant`. */
export async function fetchCellJobs(signal?: AbortSignal, tenant?: string): Promise<CellResult<CellJob[]>> {
  const query = tenant === undefined ? '' : `?tenant=${encodeURIComponent(tenant)}`
  const result = await fetchCellRoute<{ items: CellJob[] }>(`/api/cell/jobs${query}`, 'jobs', signal)
  return result.kind === 'ok' ? { kind: 'ok', value: result.value.items } : result
}

/** Loads every tenant of the cell, ordered by id. */
export async function fetchCellTenants(signal?: AbortSignal): Promise<CellResult<CellTenant[]>> {
  const result = await fetchCellRoute<{ items: CellTenant[] }>('/api/cell/tenants', 'tenants', signal)
  return result.kind === 'ok' ? { kind: 'ok', value: result.value.items } : result
}

/** Loads one tenant with its quotas. The value is `null` when the cell has no tenant with that id. */
export function fetchCellTenant(id: string, signal?: AbortSignal): Promise<CellResult<CellTenantDetail | null>> {
  return fetchCellRoute<CellTenantDetail | null>(`/api/cell/tenants/${encodeURIComponent(id)}`, 'tenant', signal, true)
}
