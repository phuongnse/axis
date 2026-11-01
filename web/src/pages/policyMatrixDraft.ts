import type { StudioMatrixRole, StudioMatrixTarget } from '../platform/studio'
import type { JsonSchema } from '../platform/studioSchemas'
import { uniqueStepName } from './processPalette'

// The file the server embeds, loaded with the page, so a grant's checkboxes need no loading state.
const policySchema = Object.values(
  import.meta.glob<JsonSchema>('../../../src/Axis.Configuration/Schemas/policy.schema.json', {
    eager: true,
    import: 'default',
  }),
)[0]

const targetKeys = ['entity', 'dataSource', 'process', 'endpoint'] as const

/** The key of a grant that names a target of the given kind. It is the kind itself. */
export function grantTargetKey(kind: StudioMatrixTarget['kind']): (typeof targetKeys)[number] {
  return kind
}

/** The actions a grant on a target of the given kind takes, as the policy schema lists them. */
export function grantActions(schema: JsonSchema, kind: StudioMatrixTarget['kind']): string[] {
  const values = schema.$defs?.[`${kind}Action`]?.enum
  return Array.isArray(values) ? values.filter((value): value is string => typeof value === 'string') : []
}

/** The actions a grant on a target of the given kind takes, from `policy.schema.json`. */
export function targetActions(kind: StudioMatrixTarget['kind']): string[] {
  return grantActions(policySchema, kind)
}

/**
 * The indexes of the grants of a policy that belong to a matrix target. A grant counts when it names exactly one
 * target and that target is of the target's kind and has its name, ignoring letter case. Other grants stay as written.
 */
export function ownGrantIndexes(grants: unknown[], target: Pick<StudioMatrixTarget, 'kind' | 'name'>): number[] {
  const key = grantTargetKey(target.kind)
  const indexes: number[] = []
  grants.forEach((grant, index) => {
    if (typeof grant !== 'object' || grant === null || Array.isArray(grant)) {
      return
    }
    const record = grant as Record<string, unknown>
    if (targetKeys.filter((candidate) => Object.hasOwn(record, candidate)).length !== 1) {
      return
    }
    const value = record[key]
    if (typeof value === 'string' && value.toLowerCase() === target.name.toLowerCase()) {
      indexes.push(index)
    }
  })
  return indexes
}

/** A `<Role>Access` policy that names only the role and has no grant yet. */
export function newPolicy(role: Pick<StudioMatrixRole, 'name' | 'accessPolicy'>): Record<string, unknown> {
  return {
    id: crypto.randomUUID(),
    kind: 'policy',
    name: role.accessPolicy.name,
    formatVersion: 1,
    roles: [role.name],
    grants: [],
  }
}

/**
 * A grant of a target with no action yet. Its name is the target's name after its last `/`, with the first letter
 * lowercased, plus the first free number among the names of `grants`, ignoring letter case.
 */
export function newGrant(
  target: Pick<StudioMatrixTarget, 'kind' | 'name'>,
  grants: readonly unknown[],
): Record<string, unknown> {
  const local = target.name.slice(target.name.lastIndexOf('/') + 1)
  const base = `${local.slice(0, 1).toLowerCase()}${local.slice(1)}`.slice(0, 56) || 'grant'
  return { name: uniqueStepName(grants, base), [grantTargetKey(target.kind)]: target.name, actions: [] }
}
