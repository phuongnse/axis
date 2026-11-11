import { apiFetch } from './api'

/** Which outside MCP clients may connect, and the client ids that may under `allowList`, as `GET /api/tenant/outside-clients` serves them. */
export interface TenantOutsideClients {
  outsideClients: 'all' | 'allowList' | 'none'
  allowedClients: string[]
}

/** Reads the setting. Any status but `200` gives `null`, and a status other than `404` or `403` is logged. */
export async function fetchTenantOutsideClients(signal?: AbortSignal): Promise<TenantOutsideClients | null> {
  const response = await apiFetch('/api/tenant/outside-clients', { signal }, { promptSignInOnUnauthorized: false })
  if (!response.ok) {
    if (response.status !== 404 && response.status !== 403) {
      console.warn(`Reading the outside clients setting failed with status ${response.status}.`)
    }
    return null
  }
  return (await response.json()) as TenantOutsideClients
}

/**
 * Replaces both values. `ok` when the server answers `204`, and `notApplied` on `502`, where the server stored the
 * change but the identity provider did not apply it, so saving again applies it. Any other answer is `failed`.
 */
export async function setTenantOutsideClients(value: TenantOutsideClients): Promise<'ok' | 'notApplied' | 'failed'> {
  const response = await apiFetch('/api/tenant/outside-clients', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(value),
  })
  if (response.status === 204) {
    return 'ok'
  }
  return response.status === 502 ? 'notApplied' : 'failed'
}

/** A person who consented to an outside MCP client, as `GET /api/tenant/connected-apps` serves them. */
export interface TenantConnectedAppPerson {
  userId: string
  displayName: string
  email: string | null
  consentedAt: string
}

/** An outside MCP client that at least one person consented to, as `GET /api/tenant/connected-apps` serves it. */
export interface TenantConnectedApp {
  clientId: string
  name: string | null
  lastUsedAt: string | null
  people: TenantConnectedAppPerson[]
}

/** Reads the clients with consent. Any status but `200` gives `null`, and a status other than `404` or `403` is logged. */
export async function fetchConnectedApps(signal?: AbortSignal): Promise<TenantConnectedApp[] | null> {
  const response = await apiFetch('/api/tenant/connected-apps', { signal }, { promptSignInOnUnauthorized: false })
  if (!response.ok) {
    if (response.status !== 404 && response.status !== 403) {
      console.warn(`Reading the connected apps failed with status ${response.status}.`)
    }
    return null
  }
  return ((await response.json()) as { items: TenantConnectedApp[] }).items
}

/**
 * Removes every person's consent to the client. `ok` when the server answers `204`, and `notFinished` on `502`, where
 * the server stored the revoke but the identity provider did not finish removing the consents, so revoking again
 * finishes it. Any other answer is `failed`.
 */
export async function revokeConnectedApp(clientId: string): Promise<'ok' | 'notFinished' | 'failed'> {
  const response = await apiFetch(`/api/tenant/connected-apps/${encodeURIComponent(clientId)}/revoke`, {
    method: 'POST',
  })
  if (response.status === 204) {
    return 'ok'
  }
  return response.status === 502 ? 'notFinished' : 'failed'
}
