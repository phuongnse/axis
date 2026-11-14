import { apiFetch } from './api'
import { quotaRefusalTitle } from './quota'

/** Whether the tenant's application packages are on, as `GET /api/tenant/application-packages` serves it. */
export interface TenantApplicationPackages {
  on: boolean
}

/** Reads the switch. Any status but `200` gives `null`, and a status other than `404` or `403` is logged. */
export async function fetchTenantApplicationPackages(signal?: AbortSignal): Promise<TenantApplicationPackages | null> {
  const response = await apiFetch('/api/tenant/application-packages', { signal }, { promptSignInOnUnauthorized: false })
  if (!response.ok) {
    if (response.status !== 404 && response.status !== 403) {
      console.warn(`Reading the application packages switch failed with status ${response.status}.`)
    }
    return null
  }
  return (await response.json()) as TenantApplicationPackages
}

/** Turns the switch on or off. A refusal past the quota carries the problem's title, any other failure has `null`. */
export async function setTenantApplicationPackages(
  on: boolean,
): Promise<{ ok: true } | { ok: false; title: string | null }> {
  const response = await apiFetch('/api/tenant/application-packages', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ on }),
  })
  return response.status === 204 ? { ok: true } : { ok: false, title: await quotaRefusalTitle(response) }
}
