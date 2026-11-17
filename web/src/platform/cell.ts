import { apiFetch } from './api'
import { fileNameOf } from './exports'
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
  /** The scheduled purge time of a `deleting` tenant, and `null` for every other. */
  deleteAfter: string | null
}

/** One quota of a tenant. `value` is `null` for no limit, and `overridden` is true when the tenant overrides the cell's default. */
export interface CellQuota {
  value: number | null
  overridden: boolean
}

/** The newest request for one host of a tenant's own. `expiresAt` is an ISO 8601 time. */
export interface CellHostRequestState {
  host: string
  state: 'pending' | 'verified' | 'expired'
  expiresAt: string
}

/**
 * One tenant with its quotas, its usage of the last 13 months by day, newest first, and the newest request of each
 * host that was asked for, ordered by host, as served by `GET /api/cell/tenants/{id}`.
 */
export interface CellTenantDetail extends CellTenant {
  quotas: Record<string, CellQuota>
  days: TenantUsageDay[]
  hostRequests: CellHostRequestState[]
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

/** One page of the jobs of the cell, as served by `GET /api/cell/jobs`. `totalCount` is the number of jobs that matched before the page was cut. */
export interface CellJobPage {
  items: CellJob[]
  page: number
  pageSize: number
  totalCount: number
}

/**
 * Loads one page of the jobs of the cell, newest first unless the query sorts. `query` is the record API's paging, sort, search and
 * filter query string, and may add `tenant` to keep one tenant's jobs.
 */
export function fetchCellJobs(query: string, signal?: AbortSignal): Promise<CellResult<CellJobPage>> {
  return fetchCellRoute<CellJobPage>(`/api/cell/jobs${query ? `?${query}` : ''}`, 'jobs', signal)
}

/** One page of the tenants of the cell, as served by `GET /api/cell/tenants`. `totalCount` is the number of tenants that matched before the page was cut. */
export interface CellTenantPage {
  items: CellTenant[]
  page: number
  pageSize: number
  totalCount: number
}

/** Loads one page of the tenants of the cell. `query` is the record API's paging, sort, search and filter query string. */
export function fetchCellTenants(query: string, signal?: AbortSignal): Promise<CellResult<CellTenantPage>> {
  return fetchCellRoute<CellTenantPage>(`/api/cell/tenants${query ? `?${query}` : ''}`, 'tenants', signal)
}

/** One page of a tenant's usage days, as served by `GET /api/cell/tenants/{id}/usage-days`. `totalCount` is the number of days that matched before the page was cut. */
export interface CellTenantUsageDayPage {
  items: TenantUsageDay[]
  page: number
  pageSize: number
  totalCount: number
}

/** Loads one page of a tenant's usage days. `query` is the record API's paging, sort and filter query string. */
export function fetchCellTenantUsageDays(
  id: string,
  query: string,
  signal?: AbortSignal,
): Promise<CellResult<CellTenantUsageDayPage>> {
  return fetchCellRoute<CellTenantUsageDayPage>(
    `/api/cell/tenants/${encodeURIComponent(id)}/usage-days${query ? `?${query}` : ''}`,
    'tenant usage days',
    signal,
  )
}

/** Loads one tenant with its quotas. The value is `null` when the cell has no tenant with that id. */
export function fetchCellTenant(id: string, signal?: AbortSignal): Promise<CellResult<CellTenantDetail | null>> {
  return fetchCellRoute<CellTenantDetail | null>(`/api/cell/tenants/${encodeURIComponent(id)}`, 'tenant', signal, true)
}

/** A purged main tenant: when the purge ran, and when its deletion is complete, as the point-in-time recovery no longer holds it. */
export interface CellDeletedTenant {
  id: string
  deletedAt: string
  completeAt: string
}

/** Loads a purged tenant. The value is `null` when the cell has no purged main tenant with that id. */
export function fetchCellDeletedTenant(
  id: string,
  signal?: AbortSignal,
): Promise<CellResult<CellDeletedTenant | null>> {
  return fetchCellRoute<CellDeletedTenant | null>(
    `/api/cell/deleted-tenants/${encodeURIComponent(id)}`,
    'deleted tenant',
    signal,
    true,
  )
}

/** Loads one job with its steps. */
export function fetchCellJob(id: string, signal?: AbortSignal): Promise<CellResult<CellJob>> {
  return fetchCellRoute<CellJob>(`/api/cell/jobs/${encodeURIComponent(id)}`, 'job', signal)
}

/** The actions of the Tenant view that queue a job, named by the kind of the job. */
export type CellTenantAction = 'suspend' | 'resume' | 'export' | 'delete' | 'cancel-deletion'

/**
 * The answer of a console route that writes: the data, the refusal that the console shows, or the problem that the
 * route answered with a final 4xx status.
 */
export type CellWriteResult<T> = CellResult<T> | { kind: 'problem'; status: number; title: string }

/** The answer of {@link queueCellTenantJob}: the queued job, a refusal or a problem. */
export type CellActionResult = CellWriteResult<CellJob>

/**
 * Reads the answer of a console route that writes. A 401 or 403 is a refusal. Any other 4xx is a final answer and comes
 * back as the problem. A 5xx throws, so the caller may retry. A 2xx answers its JSON body.
 */
export async function cellWriteAnswer<T>(response: Response, name: string): Promise<CellWriteResult<T>> {
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
    throw new Error(`${name} failed with status ${response.status}.`)
  }
  return { kind: 'ok', value: (await response.json()) as T }
}

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
  const result = await cellWriteAnswer<Omit<CellJob, 'steps' | 'error'> & Partial<CellJob>>(
    response,
    'Queuing the cell job',
  )
  if (result.kind !== 'ok') {
    return result
  }
  const queued = result.value
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
    export: { method: 'POST', url: `${tenantUrl}/exports` },
    delete: { method: 'DELETE', url: tenantUrl },
    'cancel-deletion': { method: 'POST', url: `${tenantUrl}/deletion/cancel` },
  }
  return sendCellJob(requests[action].method, requests[action].url, idempotencyKey)
}

/**
 * Queues a new job from the failed job `id`, see {@link sendCellJob}. The route answers 409 for a job that is not
 * failed, a `purge` job or a job with a newer job of its kind. The request has no body.
 */
export function runCellJobAgain(id: string, idempotencyKey: string): Promise<CellActionResult> {
  return sendCellJob('POST', `/api/cell/jobs/${encodeURIComponent(id)}/run-again`, idempotencyKey)
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

/** The quota changes of {@link setCellTenantQuotas}: `set` maps a quota key to its value, where `null` is no limit, and `remove` lists the keys that go back to the cell's default. */
export interface CellQuotaChanges {
  set: Record<string, number | null>
  remove: string[]
}

/**
 * Sets and removes quotas of the tenant `id` and answers the tenant's quotas after the change. A 401 or 403 is a
 * refusal and any other 4xx comes back as the problem. A 5xx and a network error throw.
 */
export async function setCellTenantQuotas(
  id: string,
  changes: CellQuotaChanges,
): Promise<CellWriteResult<Record<string, CellQuota>>> {
  const response = await apiFetch(
    `/api/cell/tenants/${encodeURIComponent(id)}/quotas`,
    { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(changes) },
    { promptSignInOnUnauthorized: false },
  )
  const result = await cellWriteAnswer<{ quotas: Record<string, CellQuota> }>(
    response,
    'Setting the cell tenant quotas',
  )
  return result.kind === 'ok' ? { kind: 'ok', value: result.value.quotas } : result
}

/** A pending request for a host of a tenant's own, with the DNS records that the customer must add. */
export interface CellHostRequest {
  host: string
  token: string
  state: string
  /** When the request expires, as an ISO 8601 time. */
  expiresAt: string
  txt: { name: string; value: string }
  cname: { name: string; target: string }
}

/**
 * Requests the host `host` for the tenant `id` and answers the DNS records to add. A 401 or 403 is a refusal and any
 * other 4xx, such as a host that another tenant holds, comes back as the problem. A 5xx and a network error throw.
 */
export async function requestCellTenantHost(id: string, host: string): Promise<CellWriteResult<CellHostRequest>> {
  const response = await apiFetch(
    `/api/cell/tenants/${encodeURIComponent(id)}/hosts`,
    { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ host }) },
    { promptSignInOnUnauthorized: false },
  )
  return cellWriteAnswer<CellHostRequest>(response, 'Requesting the cell tenant host')
}

/**
 * Downloads the ZIP of the export `exportId` of the tenant `tenantId`, with `Accept: application/zip`. A 200 answers the
 * file with the name the server gave it. A 401 or 403 is a refusal. Any other 4xx, such as a 410 for an export past its
 * retention, comes back as the problem with the server's title. A 5xx and a network error throw.
 */
export async function downloadCellTenantExport(
  tenantId: string,
  exportId: string,
): Promise<CellResult<{ blob: Blob; fileName: string }> | { kind: 'problem'; status: number; title: string }> {
  const response = await apiFetch(
    `/api/cell/tenants/${encodeURIComponent(tenantId)}/exports/${encodeURIComponent(exportId)}`,
    { headers: { Accept: 'application/zip' } },
    { promptSignInOnUnauthorized: false },
  )
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
    throw new Error(`Downloading the cell tenant export failed with status ${response.status}.`)
  }
  return {
    kind: 'ok',
    value: {
      blob: await response.blob(),
      fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? `${tenantId}.zip`,
    },
  }
}
