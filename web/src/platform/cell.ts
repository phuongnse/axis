import { apiFetch } from './api'
import type { TenantQuotaUse, TenantUsageDay } from './tenantUsage'

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

/** What a tenant uses against its limits. Each `used` is `null` while nothing fills it. */
export interface CellTenantUsage {
  storage: TenantQuotaUse
  users: TenantQuotaUse
  applications: TenantQuotaUse
  emailsPerDay: TenantQuotaUse
}

/**
 * One tenant of the cell, as served by `GET /api/cell/tenants`. `failedWork` is `null` until the worker has counted the
 * tenant, and `newestArchiveAt`, an ISO 8601 time, is `null` for a tenant that was never backed up.
 */
export interface CellTenant {
  id: string
  status: CellTenantStatus
  application: string | null
  hosts: string[]
  failedWork: CellFailedWork | null
  usage: CellTenantUsage
  newestArchiveAt: string | null
}

/** One quota of a tenant. `value` is `null` for no limit, and `overridden` is true when the tenant overrides the cell's default. */
export interface CellQuota {
  value: number | null
  overridden: boolean
}

/** One tenant with its quotas and its usage of the last 13 months by day, newest first, as served by `GET /api/cell/tenants/{id}`. */
export interface CellTenantDetail extends CellTenant {
  quotas: Record<string, CellQuota>
  days: TenantUsageDay[]
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

/** Loads one job with its steps. */
export function fetchCellJob(id: string, signal?: AbortSignal): Promise<CellResult<CellJob>> {
  return fetchCellRoute<CellJob>(`/api/cell/jobs/${encodeURIComponent(id)}`, 'job', signal)
}

/** The actions of the Tenant view that queue a job, named by the kind of the job. */
export type CellTenantAction = 'suspend' | 'resume' | 'delete' | 'cancel-deletion'

/**
 * The answer of {@link queueCellTenantJob} and {@link createCellTenant}: the queued job, the refusal that the console shows, or the problem that the
 * route answered with a final 4xx status.
 */
export type CellActionResult = CellResult<CellJob> | { kind: 'problem'; status: number; title: string }

/**
 * Sends a request that queues a job, with `idempotencyKey` as its `Idempotency-Key`. A 202 answers the queued job, whose
 * `steps` are empty and `error` is `null`, because the 202 holds neither. A 401 or 403 is a refusal. Any other 4xx is a
 * final answer and comes back as the problem. A 5xx and a network error throw, so the caller may retry with the same
 * key. A `body` is sent as JSON, exactly as given, because the server hashes the body into the request that the key
 * names.
 */
async function sendCellJob(
  method: 'POST' | 'DELETE',
  url: string,
  idempotencyKey: string,
  body?: string,
): Promise<CellActionResult> {
  const headers: Record<string, string> = { 'Idempotency-Key': idempotencyKey }
  if (body !== undefined) {
    headers['Content-Type'] = 'application/json'
  }
  const response = await apiFetch(url, { method, headers, body }, { promptSignInOnUnauthorized: false })
  if (response.status === 401) {
    return { kind: 'unauthorized' }
  }
  if (response.status === 403) {
    return { kind: 'forbidden' }
  }
  if (response.status >= 400 && response.status < 500) {
    const problem = (await response.json().catch(() => ({}))) as { title?: string }
    return { kind: 'problem', status: response.status, title: problem.title ?? '' }
  }
  if (!response.ok) {
    throw new Error(`Queuing the cell job failed with status ${response.status}.`)
  }
  const queued = (await response.json()) as Omit<CellJob, 'steps' | 'error'> & Partial<CellJob>
  return { kind: 'ok', value: { ...queued, steps: queued.steps ?? [], error: queued.error ?? null } }
}

/**
 * Queues the job of `action` for the tenant `id`, with the route of the provisioning API that the action names, see
 * {@link sendCellJob}. The request has no body.
 */
export function queueCellTenantJob(
  id: string,
  action: CellTenantAction,
  idempotencyKey: string,
): Promise<CellActionResult> {
  const tenantUrl = `/api/cell/tenants/${encodeURIComponent(id)}`
  const requests: Record<CellTenantAction, { method: 'POST' | 'DELETE'; url: string }> = {
    suspend: { method: 'POST', url: `${tenantUrl}/suspend` },
    resume: { method: 'POST', url: `${tenantUrl}/resume` },
    delete: { method: 'DELETE', url: tenantUrl },
    'cancel-deletion': { method: 'POST', url: `${tenantUrl}/deletion/cancel` },
  }
  return sendCellJob(requests[action].method, requests[action].url, idempotencyKey)
}

/** The fields of a tenant to create. */
export interface CellTenantRequest {
  id: string
  application: string
  administrator: string
}

/**
 * Queues the job that creates a tenant, see {@link sendCellJob}. The caller builds `body` once, as the JSON of a
 * {@link CellTenantRequest}, so a retry sends the same bytes under the same `idempotencyKey`.
 */
export function createCellTenant(body: string, idempotencyKey: string): Promise<CellActionResult> {
  return sendCellJob('POST', '/api/cell/tenants', idempotencyKey, body)
}
