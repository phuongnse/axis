import { apiFetch } from './api'
import { fileNameOf } from './exports'

/** A tenant export, as `POST /api/tenant/exports` and `GET /api/tenant/exports/{id}` serve it. `expiresAt` is `null` until it succeeds. */
export interface TenantExport {
  id: string
  tenantId: string
  state: 'queued' | 'running' | 'succeeded' | 'failed'
  createdAt: string
  expiresAt: string | null
}

/** The outcome of reading an export: the export, or that the server has no such export any more (`404` or `410`). */
export type TenantExportRead = { kind: 'ok'; export: TenantExport } | { kind: 'gone' }

/** The outcome of the download: the file with its name, or the refused response's status. */
export type TenantExportFile = { ok: true; blob: Blob; fileName: string } | { ok: false; status: number }

/** Starts an export of the tenant, or returns the one that is already queued or running. Anything but `200` or `202` throws. */
export async function startTenantExport(): Promise<TenantExport> {
  const response = await apiFetch('/api/tenant/exports', { method: 'POST' })
  if (response.status !== 200 && response.status !== 202) {
    throw new Error(`Starting the export failed with status ${response.status}.`)
  }
  return (await response.json()) as TenantExport
}

/** Reads the export. Any status but `200`, `404` and `410` throws. */
export async function readTenantExport(id: string, signal?: AbortSignal): Promise<TenantExportRead> {
  const response = await apiFetch(`/api/tenant/exports/${encodeURIComponent(id)}`, { signal })
  if (response.status === 404 || response.status === 410) {
    return { kind: 'gone' }
  }
  if (!response.ok) {
    throw new Error(`Reading the export failed with status ${response.status}.`)
  }
  return { kind: 'ok', export: (await response.json()) as TenantExport }
}

/** Downloads the ZIP of a succeeded export, with the name the server gave it. A refused response comes back as `{ ok: false, status }`. */
export async function downloadTenantExport(id: string): Promise<TenantExportFile> {
  const response = await apiFetch(`/api/tenant/exports/${encodeURIComponent(id)}`, {
    headers: { Accept: 'application/zip' },
  })
  if (!response.ok) {
    return { ok: false, status: response.status }
  }
  return {
    ok: true,
    blob: await response.blob(),
    fileName: fileNameOf(response.headers.get('Content-Disposition')) ?? 'export.zip',
  }
}
