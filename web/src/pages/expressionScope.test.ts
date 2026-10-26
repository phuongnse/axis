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

  it('offers a form condition only the fields the form lists', () => {
    const draft = {
      entity: 'purchaserequest',
      sections: [{ fields: [{ field: 'Title' }, { field: 'unknown' }, 3, null] }, { fields: 'broken' }, null],
    }
    const labels = labelsOf(expressionScope('form', draft, '/sections/0/fields/0/requiredWhen', names))

    expect(labels).toContain('title')
    expect(labels).not.toContain('requester')
    expect(labels).not.toContain('code')
    expect(labels).toContain('length()')
  })

  it('offers a form condition no field while the form lists none', () => {
    const draft = { entity: 'PurchaseRequest', sections: 'broken' }

    expect(labelsOf(expressionScope('form', draft, '/sections/0/visibleWhen', names))).not.toContain('title')
  })

  it('offers a process with a subject its id and its entity fields, id first', () => {
    const labels = labelsOf(
      expressionScope('process', { entity: 'PurchaseRequest' }, '/steps/0/branches/0/when', names),
    )

    expect(labels.slice(0, 3)).toEqual(['id', 'title', 'requester'])
    expect(labels).toContain('departments:IsManager()')
  })

  it('offers a process without a subject its parameters and the rules, and no field or id', () => {
    const draft = { parameters: [{ name: 'daysOverdue', type: 'integer' }, { type: 'text' }] }
    const options = expressionScope('process', draft, '/steps/0/branches/0/when', names)
    const labels = labelsOf(options)

    expect(options.find((option) => option.label === 'daysOverdue')?.type).toBe('parameter')
    expect(labels).toContain('departments:IsManager()')
    expect(labels).toContain('length()')
    expect(labels).not.toContain('id')
    expect(labels).not.toContain('title')
    expect(labels).not.toContain('code')
  })

  it('does not offer the parameters of a process that also has an entity', () => {
    const draft = { entity: 'PurchaseRequest', parameters: [{ name: 'daysOverdue', type: 'integer' }] }

    expect(labelsOf(expressionScope('process', draft, '/steps/0/branches/0/when', names))).not.toContain('daysOverdue')
  })

  it('offers a data change trigger the fields and id of its entity', () => {
    const draft = { type: 'dataChange', entity: 'PurchaseRequest', on: ['delete'] }
    const labels = labelsOf(expressionScope('trigger', draft, '/parameters/daysOverdue', names))

    expect(labels.slice(0, 3)).toEqual(['id', 'title', 'requester'])
    expect(labels).toContain('departments:IsManager()')
  })

  it('offers a trigger without an entity no field', () => {
    const labels = labelsOf(expressionScope('trigger', { type: 'event', event: 'Paid' }, '/when', names))

    expect(labels).not.toContain('id')
    expect(labels).toContain('departments:IsManager()')
  })

  it('offers only the functions and aggregates when no names are loaded', () => {
    const options = expressionScope('policy', { grants: [{ entity: 'PurchaseRequest' }] }, '/grants/0/filter', null)

    expect(options.every((option) => option.type === 'function')).toBe(true)
    expect(labelsOf(options)).toContain('count()')
    expect(labelsOf(options)).toContain('avg()')
    expect(labelsOf(options)).toContain('currentUser()')
  })

  it('keeps a call with no argument outside its brackets', () => {
    const options = expressionScope('entity', {}, '/validations/0/expression', names)

    expect(options.find((option) => option.label === 'now()')?.cursorInside).toBe(false)
    expect(options.find((option) => option.label === 'length()')?.cursorInside).toBe(true)
  })

  it('offers today() with the cursor after the brackets and toDate() with it inside', () => {
    const options = expressionScope('entity', {}, '/validations/0/expression', names)

    expect(options.find((option) => option.label === 'today()')?.cursorInside).toBe(false)
    expect(options.find((option) => option.label === 'toDate()')?.cursorInside).toBe(true)
  })

  it('ignores a draft that is malformed', () => {
    const options = expressionScope('policy', { grants: 'nope' }, '/grants/0/filter', names)

    expect(labelsOf(options)).not.toContain('requester')
    expect(labelsOf(options)).toContain('length()')
  })
})
