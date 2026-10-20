import type { StudioMatrixRole, StudioMatrixTarget } from '../platform/studio'

const targetKeys = ['entity', 'dataSource', 'process', 'endpoint'] as const

/** The key of a grant that names a target of the given kind. It is the kind itself. */
export function grantTargetKey(kind: StudioMatrixTarget['kind']): (typeof targetKeys)[number] {
  return kind
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

/** A grant of a target with no action yet. */
export function newGrant(target: Pick<StudioMatrixTarget, 'kind' | 'name'>): Record<string, unknown> {
  return { [grantTargetKey(target.kind)]: target.name, actions: [] }
}
