import { describe, expect, it } from 'vitest'
import { contractView } from './contractView'

/** The `RequestContract` of `docs/reference/module-contracts.md`, without the texts. */
const requestContract = {
  entity: 'Request',
  lifecycle: {
    field: 'status',
    states: [
      { name: 'draft', initial: true, deletable: true },
      { name: 'submitted' },
      { name: 'returned', deletable: true },
      { name: 'approved' },
      { name: 'rejected' },
    ],
    transitions: [
      { name: 'submit', from: ['draft', 'returned'], to: 'submitted', guard: 'accountableUser() == requester' },
      { name: 'approve', from: ['submitted'], to: 'approved', guard: 'accountableUser() != null' },
      { name: 'reject', from: ['submitted'], to: 'rejected', guard: 'accountableUser() != null' },
      { name: 'return', from: ['submitted'], to: 'returned' },
    ],
  },
  validations: [{ name: 'amountFrozenAfterSubmit', expression: 'amount == previous(amount)', field: 'amount' }],
  effects: [
    { name: 'approvalRecorded', on: 'approve', create: { entity: 'Decision', values: { outcome: "'approved'" } } },
  ],
  ceiling: {
    slots: {
      requester: [{ actions: ['read', 'create'], fields: ['title', 'amount'], filter: 'requester == currentUser()' }],
      approver: [{ actions: ['read'] }, { actions: ['update'], fields: ['status'], filter: "status == 'submitted'" }],
    },
    others: [{ actions: ['read'] }],
  },
}

describe('contractView', () => {
  it('lists the states with the points that exist', () => {
    const { states } = contractView(requestContract)
    expect(states.map((state) => [state.name, state.initial, state.deletable])).toEqual([
      ['draft', true, true],
      ['submitted', false, false],
      ['returned', false, true],
      ['approved', false, false],
      ['rejected', false, false],
    ])
    expect(states[0].points).toEqual([])
    expect(states[1].points).toEqual(['Request/states/submitted/initial', 'Request/states/submitted/deletable'])
    expect(states[2].points).toEqual(['Request/states/returned/initial'])
  })

  it('lists the transitions and one edge for each from state', () => {
    const { transitions, edges } = contractView(requestContract)
    expect(transitions.map((transition) => transition.point)).toEqual([
      'Request/transitions/submit',
      'Request/transitions/approve',
      'Request/transitions/reject',
      'Request/transitions/return',
    ])
    expect(transitions[3].guard).toBeNull()
    expect(edges.map((edge) => [edge.id, edge.to, edge.guard])).toEqual([
      ['submit:draft', 'submitted', true],
      ['submit:returned', 'submitted', true],
      ['approve:submitted', 'approved', true],
      ['reject:submitted', 'rejected', true],
      ['return:submitted', 'returned', false],
    ])
  })

  it('lists the validations, the effects and the ceiling entries with their points', () => {
    const { validations, effects, ceiling } = contractView(requestContract)
    expect(validations).toEqual([
      {
        name: 'amountFrozenAfterSubmit',
        field: 'amount',
        expression: 'amount == previous(amount)',
        point: 'Request/validations/amountFrozenAfterSubmit',
      },
    ])
    expect(effects).toEqual([
      { name: 'approvalRecorded', on: 'approve', entity: 'Decision', point: 'Request/effects/approvalRecorded' },
    ])
    expect(ceiling.map((row) => [row.slot, row.actions.join(','), row.point])).toEqual([
      ['requester', 'read,create', 'Request/ceiling/requester'],
      ['approver', 'read', 'Request/ceiling/approver'],
      ['approver', 'update', 'Request/ceiling/approver'],
      ['others', 'read', 'Request/ceiling/others'],
    ])
    expect(ceiling[0]).toMatchObject({ fields: ['title', 'amount'], filter: 'requester == currentUser()' })
    expect(ceiling[1]).toMatchObject({ fields: [], filter: '' })
  })

  it('tolerates a file with missing or odd values', () => {
    expect(contractView({})).toEqual({
      entity: '',
      states: [],
      transitions: [],
      edges: [],
      validations: [],
      effects: [],
      ceiling: [],
    })
    const odd = contractView({
      entity: 'A',
      lifecycle: { states: [1, null], transitions: 'x' },
      ceiling: { slots: [] },
    })
    expect(odd.states).toHaveLength(2)
    expect(odd.transitions).toEqual([])
    expect(odd.ceiling).toEqual([])
  })
})
