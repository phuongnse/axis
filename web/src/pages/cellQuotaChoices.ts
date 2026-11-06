import type { CellQuota, CellQuotaChanges } from '../platform/cell'
import { setOwn } from '../platform/ownProperty'

/** What the operator chooses for one quota: a value, no limit, or the cell's default. */
export type QuotaChoiceKind = 'value' | 'none' | 'default'

export interface QuotaChoice {
  kind: QuotaChoiceKind
  /** The number to set, for the `value` choice. `null` while the field is empty. */
  value: number | null
}

/** The choice that shows `quota` as it is now: not overridden is the default, overridden with no value is no limit. */
export function currentChoice(quota: CellQuota): QuotaChoice {
  if (!quota.overridden) {
    return { kind: 'default', value: quota.value }
  }
  return quota.value === null ? { kind: 'none', value: null } : { kind: 'value', value: quota.value }
}

/**
 * Builds the body of the quotas request from the choices. It names only the keys whose choice differs from the current
 * quota. A value goes in `set` as the number, no limit goes in `set` as `null`, and the default of an overridden key goes
 * in `remove`.
 */
export function quotaChanges(
  quotas: Record<string, CellQuota>,
  choices: Record<string, QuotaChoice>,
): CellQuotaChanges {
  const changes: CellQuotaChanges = { set: {}, remove: [] }
  for (const [key, quota] of Object.entries(quotas)) {
    const choice = choices[key]
    if (!choice) {
      continue
    }
    if (choice.kind === 'default') {
      if (quota.overridden) {
        changes.remove.push(key)
      }
    } else if (choice.kind === 'none') {
      if (!quota.overridden || quota.value !== null) {
        setOwn(changes.set, key, null)
      }
    } else if (choice.value !== null && (!quota.overridden || quota.value !== choice.value)) {
      setOwn(changes.set, key, choice.value)
    }
  }
  return changes
}
