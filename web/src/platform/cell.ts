import { apiFetch } from './api'

/** The signed-in platform operator, as served by `GET /api/cell/session`. */
export interface CellSession {
  displayName: string
}

/** The cell, as served by `GET /api/cell`. `cap` is `null` when the cell has no cap on its tenants. */
export interface Cell {
  id: string
  region: string
  tenants: number
  cap: number | null
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

/**
 * The answer of a console route: the data, or the refusal that the console shows. `unauthorized` means that the
 * session is missing or has expired, and `forbidden` that the person lacks the operator role.
 */
export type CellResult<T> = { kind: 'ok'; value: T } | { kind: 'unauthorized' } | { kind: 'forbidden' }

/** Reads a console route. A 401 never shows the tenant sign-in prompt, as it starts the wrong realm's sign-in. */
async function fetchCellRoute<T>(url: string, name: string, signal?: AbortSignal): Promise<CellResult<T>> {
  const response = await apiFetch(url, { signal }, { promptSignInOnUnauthorized: false })
  if (response.status === 401) {
    return { kind: 'unauthorized' }
  }
  if (response.status === 403) {
    return { kind: 'forbidden' }
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

/** Loads the newest jobs of the cell, newest first. */
export async function fetchCellJobs(signal?: AbortSignal): Promise<CellResult<CellJob[]>> {
  const result = await fetchCellRoute<{ items: CellJob[] }>('/api/cell/jobs', 'jobs', signal)
  return result.kind === 'ok' ? { kind: 'ok', value: result.value.items } : result
}
