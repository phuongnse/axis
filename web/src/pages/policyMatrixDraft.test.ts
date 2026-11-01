import { describe, expect, it } from 'vitest'
import { grantActions, newGrant, newPolicy, ownGrantIndexes, targetActions } from './policyMatrixDraft'

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

describe('grantActions and targetActions', () => {
  it("takes each kind's actions from the schema it is given", () => {
    const schema = {
      $defs: {
        entityAction: { enum: ['wave', 'bow'] },
        dataSourceAction: { enum: ['peek'] },
        processAction: { enum: [] },
      },
    }

    expect(grantActions(schema, 'entity')).toEqual(['wave', 'bow'])
    expect(grantActions(schema, 'dataSource')).toEqual(['peek'])
    expect(grantActions(schema, 'process')).toEqual([])
    expect(grantActions(schema, 'endpoint')).toEqual([])
    expect(grantActions({}, 'entity')).toEqual([])
  })

  it('reads policy.schema.json for the real lists', () => {
    expect(targetActions('entity')).toEqual(['read', 'create', 'update', 'delete', 'export', 'import'])
    expect(targetActions('dataSource')).toEqual(['read', 'export'])
    expect(targetActions('process')).toEqual(['start', 'inspect', 'cancel', 'retry', 'resolve', 'reassign'])
    expect(targetActions('endpoint')).toEqual(['invoke'])
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

  it('makes a grant of the target with no action and a name', () => {
    expect(newGrant({ kind: 'dataSource', name: 'OpenRequests' }, [])).toEqual({
      name: 'openRequests1',
      dataSource: 'OpenRequests',
      actions: [],
    })
  })

  it('names the grant with the first number no other grant uses, ignoring case', () => {
    expect(newGrant({ kind: 'dataSource', name: 'OpenRequests' }, [{ name: 'OpenRequests1' }]).name).toBe(
      'openRequests2',
    )
    expect(
      newGrant({ kind: 'entity', name: 'Ticket' }, [{ name: 'ticket1' }, { name: 'TICKET2' }, 'x', null, { name: 3 }])
        .name,
    ).toBe('ticket3')
  })

  it('drops a module prefix, cuts the base to 56 characters and falls back to grant', () => {
    expect(newGrant({ kind: 'process', name: 'department-budgets/BudgetApproval' }, []).name).toBe('budgetApproval1')
    expect(newGrant({ kind: 'entity', name: `A${'b'.repeat(69)}` }, []).name).toBe(`a${'b'.repeat(55)}1`)
    expect(newGrant({ kind: 'entity', name: 'module/' }, []).name).toBe('grant1')
  })
})
