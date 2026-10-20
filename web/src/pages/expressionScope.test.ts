import { describe, expect, it } from 'vitest'
import type { StudioNames } from '../platform/studio'
import { expressionScope } from './expressionScope'

const names = {
  entities: [
    {
      name: 'PurchaseRequest',
      module: null,
      fields: [
        { name: 'title', type: 'text' },
        { name: 'requester', type: 'text' },
      ],
    },
    { name: 'Department', module: null, fields: [{ name: 'code', type: 'text' }] },
  ],
  rules: [{ name: 'departments:IsManager', module: 'departments', parameters: [], resultType: 'boolean' }],
} as unknown as StudioNames

function labelsOf(options: ReturnType<typeof expressionScope>) {
  return options.map((option) => option.label)
}

describe('expressionScope', () => {
  it('offers an entity validation its own fields and the rules, but not currentUser()', () => {
    const draft = {
      fields: [
        { name: 'title', type: 'text' },
        { name: 'total', type: 'decimal' },
      ],
    }
    const labels = labelsOf(expressionScope('entity', draft, '/validations/0/expression', names))

    expect(labels).toContain('title')
    expect(labels).toContain('total')
    expect(labels).toContain('departments:IsManager()')
    expect(labels).toContain('length()')
    expect(labels).not.toContain('currentUser()')
  })

  it('offers a data source filter its parameters and its entity fields', () => {
    const draft = {
      entity: 'purchaserequest',
      parameters: [{ name: 'statusFilter', type: 'enum' }],
    }
    const options = expressionScope('dataSource', draft, '/filter', names)
    const labels = labelsOf(options)

    expect(options.find((option) => option.label === 'statusFilter')?.type).toBe('parameter')
    expect(labels).toContain('requester')
    expect(labels).toContain('title')
    expect(labels).not.toContain('code')
    expect(labels).not.toContain('currentUser()')
  })

  it('offers a policy grant filter the grant entity fields and currentUser()', () => {
    const draft = { grants: [{ entity: 'PurchaseRequest', actions: ['read'] }] }
    const labels = labelsOf(expressionScope('policy', draft, '/grants/0/filter', names))

    expect(labels).toContain('requester')
    expect(labels).toContain('currentUser()')
  })

  it('offers only the functions and aggregates when no names are loaded', () => {
    const options = expressionScope('policy', { grants: [{ entity: 'PurchaseRequest' }] }, '/grants/0/filter', null)

    expect(options.every((option) => option.type === 'function')).toBe(true)
    expect(labelsOf(options)).toContain('count()')
    expect(labelsOf(options)).toContain('currentUser()')
  })

  it('keeps a call with no argument outside its brackets', () => {
    const options = expressionScope('entity', {}, '/validations/0/expression', names)

    expect(options.find((option) => option.label === 'now()')?.cursorInside).toBe(false)
    expect(options.find((option) => option.label === 'length()')?.cursorInside).toBe(true)
  })

  it('ignores a draft that is malformed', () => {
    const options = expressionScope('policy', { grants: 'nope' }, '/grants/0/filter', names)

    expect(labelsOf(options)).not.toContain('requester')
    expect(labelsOf(options)).toContain('length()')
  })
})
