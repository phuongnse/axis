import { describe, expect, it } from 'vitest'
import { newGrant, newPolicy, ownGrantIndexes } from './policyMatrixDraft'

describe('ownGrantIndexes', () => {
  const grants = [
    { entity: 'Supplier', actions: ['read'] },
    { process: 'Supplier', actions: ['start'] },
    { entity: 'supplier', actions: ['update'] },
    { entity: 'Supplier', process: 'Other', actions: ['read'] },
    { entity: 'Other', actions: ['read'] },
    'not a grant',
    null,
  ]

  it('finds the grants of a target of the same kind, ignoring letter case', () => {
    expect(ownGrantIndexes(grants, { kind: 'entity', name: 'Supplier' })).toEqual([0, 2])
    expect(ownGrantIndexes(grants, { kind: 'process', name: 'Supplier' })).toEqual([1])
  })

  it('skips a grant that names more than one target', () => {
    expect(ownGrantIndexes(grants, { kind: 'process', name: 'Other' })).toEqual([])
  })
})

describe('newPolicy and newGrant', () => {
  it('names only the role and has no grant', () => {
    const policy = newPolicy({ name: 'matrix-tester', accessPolicy: { name: 'MatrixTesterAccess', id: null } })

    expect(Object.keys(policy)).toEqual(['id', 'kind', 'name', 'formatVersion', 'roles', 'grants'])
    expect(policy).toMatchObject({
      kind: 'policy',
      name: 'MatrixTesterAccess',
      formatVersion: 1,
      roles: ['matrix-tester'],
      grants: [],
    })
  })

  it('makes a grant of the target with no action', () => {
    expect(newGrant({ kind: 'dataSource', name: 'OpenRequests' })).toEqual({ dataSource: 'OpenRequests', actions: [] })
  })
})
