import { apiFetch } from './api'

/** Whether the tenant has its own model key, and the last four characters of it, as `GET /api/tenant/model-key` serves it. */
export interface TenantModelKey {
  set: boolean
  ending: string | null
}

/** Reads the model key. Any status but `200` gives `null`, and a status other than `404` or `403` is logged. */
export async function fetchTenantModelKey(signal?: AbortSignal): Promise<TenantModelKey | null> {
  const response = await apiFetch('/api/tenant/model-key', { signal }, { promptSignInOnUnauthorized: false })
  if (!response.ok) {
    if (response.status !== 404 && response.status !== 403) {
      console.warn(`Reading the model key failed with status ${response.status}.`)
    }
    return null
  }
  return (await response.json()) as TenantModelKey
}

/** Sets or changes the model key. `true` when the server answers `204`. */
export async function setTenantModelKey(key: string): Promise<boolean> {
  const response = await apiFetch('/api/tenant/model-key', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ key }),
  })
  return response.status === 204
}

/** Removes the model key. `true` when the server answers `204`. */
export async function removeTenantModelKey(): Promise<boolean> {
  const response = await apiFetch('/api/tenant/model-key', { method: 'DELETE' })
  return response.status === 204
}
