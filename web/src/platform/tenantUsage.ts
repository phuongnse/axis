import { apiFetch } from './api'

/** A quota's `limit`, `null` for no limit, and what the tenant `used` now, `null` when nothing counts it. */
export interface TenantQuotaUse {
  limit: number | null
  used: number | null
}

/** One day of the tenant's usage, as `GET /api/tenant/usage` serves it. Sizes are in bytes. */
export interface TenantUsageDay {
  day: string
  databaseBytes: number
  fileBytes: number
  users: number | null
  applications: number | null
  emailsSent: number
  connectorCalls: number
  workItemsRun: number
}

/** The tenant's quotas by key, and its last 30 days, newest first. */
export interface TenantUsage {
  quotas: Record<string, TenantQuotaUse>
  days: TenantUsageDay[]
}

const units = ['byte', 'kilobyte', 'megabyte', 'gigabyte'] as const

/** A size in the largest unit it fills, such as "1.5 MB", with the locale's number format. */
export function formatBytes(bytes: number, locale: string): string {
  let unitIndex = 0
  let value = bytes
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }
  return new Intl.NumberFormat(locale, {
    style: 'unit',
    unit: units[unitIndex],
    maximumFractionDigits: 1,
  }).format(value)
}

/** Reads the usage. Any status but `200` gives `null`, and a status other than `404` or `403` is logged. */
export async function fetchTenantUsage(signal?: AbortSignal): Promise<TenantUsage | null> {
  const response = await apiFetch('/api/tenant/usage', { signal }, { promptSignInOnUnauthorized: false })
  if (!response.ok) {
    if (response.status !== 404 && response.status !== 403) {
      console.warn(`Reading the usage failed with status ${response.status}.`)
    }
    return null
  }
  return (await response.json()) as TenantUsage
}
