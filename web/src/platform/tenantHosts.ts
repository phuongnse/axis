import { apiFetch } from './api'
import { cellWriteAnswer, type CellHostRequest, type CellHostRequestState, type CellWriteResult } from './cell'

/**
 * Reads the hosts that the tenant asked for, sorted by host. Any status but `200` gives `null`, and a status other than
 * `404` or `403` is logged. See docs/reference/tenant-lifecycle.md#hosts-by-a-tenant-administrator.
 */
export async function fetchTenantHosts(signal?: AbortSignal): Promise<CellHostRequestState[] | null> {
  const response = await apiFetch('/api/tenant/hosts', { signal }, { promptSignInOnUnauthorized: false })
  if (!response.ok) {
    if (response.status !== 404 && response.status !== 403) {
      console.warn(`Reading the hosts failed with status ${response.status}.`)
    }
    return null
  }
  return ((await response.json()) as { items: CellHostRequestState[] }).items
}

/**
 * Requests the host `host` for the tenant and answers the DNS records to add. A 401 or 403 is a refusal and any other
 * 4xx, such as a host that is in use, comes back as the problem. A 5xx and a network error throw.
 */
export async function requestTenantHost(host: string): Promise<CellWriteResult<CellHostRequest>> {
  const response = await apiFetch('/api/tenant/hosts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ host }),
  })
  return cellWriteAnswer<CellHostRequest>(response, 'Requesting the host')
}
