import { describe, expect, it } from 'vitest'
import type { StudioNames } from './studio'
import { getAt, labelText, parameterType, referenceOptions, referencePointer } from './studioNames'

const names = {
  locales: ['en'],
  dataSources: [
    {
      id: '4',
      name: 'Orders',
      module: null,
      parameters: [
        { name: 'status', type: 'text' },
        { name: 'limit', type: 'integer' },
      ],
      columns: [
        { name: 'openCount', type: 'integer' },
        { name: 'region', type: 'text' },
      ],
      groupBy: null,
    },
  ],
  rules: [],
  connectors: [{ id: '5', name: 'Crm', operations: [{ name: 'getAccount' }, { name: 'listAccounts' }] }],
  events: [],
  sequences: [],
  forms: [],
  pages: [],
  entities: [
    {
      id: '1',
      name: 'Order',
      labelKey: 'entities.order.label',
      module: null,
      fields: [{ name: 'Total', labelKey: 'entities.order.total.label', type: 'decimal', target: null, values: null }],
    },
    {
      id: '7',
      name: 'hr/Department',
      labelKey: 'hr.entities.department.label',
      module: 'hr',
      fields: [],
    },
  ],
  processes: [
    {
      id: '2',
      name: 'Approval',
      parameters: [{ name: 'Days', type: 'integer' }],
      steps: [
        { name: 'Review', labelKey: 'approval.review.label', type: 'task', outcomes: ['Approve'] },
        { name: 'Done', type: 'end', outcomes: [] },
      ],
    },
  ],
  roles: [{ id: '3', name: 'manager', labelKey: 'roles.manager.label' }],
  texts: [
    { key: 'orders.title', texts: { en: 'Orders' }, usedBy: 1 },
    { key: 'entities.order.label', texts: { en: 'Order', vi: 'Đơn hàng' }, usedBy: 1 },
    { key: 'hr.entities.department.label', texts: { en: 'Department', vi: 'Phòng ban' }, usedBy: 1 },
  ],
} as unknown as StudioNames

describe('referencePointer', () => {
  it('reads an absolute pointer as it is', () => {
    expect(referencePointer('/grants/0/fields/1', '/entity')).toBe('/entity')
  })

  it('drops the given number of segments from the pointer of the property', () => {
    expect(referencePointer('/grants/0/fields/1', '2/entity')).toBe('/grants/0/entity')
    expect(referencePointer('/steps/0/step', '1/process')).toBe('/steps/0/process')
  })
})

describe('getAt', () => {
  it('reads a value at a pointer and unescapes its segments', () => {
    const draft = { 'a/b': { 'c~d': 'value' } }
    expect(getAt(draft, '/a~1b/c~0d')).toBe('value')
    expect(getAt(draft, '/missing/x')).toBeUndefined()
  })
})

describe('referenceOptions', () => {
  it('lists the names of a kind', () => {
    expect(referenceOptions(names, 'entity', {}, '/entity')).toEqual([
      { name: 'Order', labelKey: 'entities.order.label' },
      { name: 'hr/Department', labelKey: 'hr.entities.department.label' },
    ])
    expect(referenceOptions(names, 'role', {}, '/roles/0')).toEqual([
      { name: 'manager', labelKey: 'roles.manager.label' },
    ])
    expect(referenceOptions(names, 'page', {}, '/page')).toEqual([])
  })

  it('gives a text key as a name only, and a connector without a label key', () => {
    expect(referenceOptions(names, 'text', {}, '/title/textKey')).toEqual([
      { name: 'orders.title' },
      { name: 'entities.order.label' },
      { name: 'hr.entities.department.label' },
    ])
    expect(referenceOptions(names, 'connector', {}, '/connector')).toEqual([{ name: 'Crm', labelKey: undefined }])
  })

  it('shows the label of a module entity from the merged texts, in the locale, else in en', () => {
    const [, department] = referenceOptions(names, 'entity', {}, '/entity')
    expect(labelText(names, department.labelKey, 'vi')).toBe('Phòng ban')
    expect(labelText(names, department.labelKey, 'fr')).toBe('Department')
    expect(labelText(names, 'hr.entities.missing.label', 'en')).toBeUndefined()
    expect(labelText(names, undefined, 'en')).toBeUndefined()
    expect(labelText(names, null, 'en')).toBeUndefined()
  })

  it('lists the fields of the entity the draft names', () => {
    const draft = { entity: 'order', grants: [{ entity: 'Order', fields: ['Total'] }] }
    expect(referenceOptions(names, { part: 'field', of: '2/entity' }, draft, '/grants/0/fields/0')).toEqual([
      { name: 'Total', labelKey: 'entities.order.total.label' },
    ])
    expect(referenceOptions(names, { part: 'field', of: '/entity' }, draft, '/fields/0')).toEqual([
      { name: 'Total', labelKey: 'entities.order.total.label' },
    ])
  })

  it('lists the steps of the process the draft names', () => {
    const draft = { name: 'Approval', steps: [{ name: 'Review', next: 'Done' }] }
    expect(referenceOptions(names, { part: 'step', of: '/name' }, draft, '/steps/0/next')).toEqual([
      { name: 'Review', labelKey: 'approval.review.label' },
      { name: 'Done', labelKey: undefined },
    ])
  })

  it('lists the parameters of the process the draft names', () => {
    const mark = { part: 'processParameter', of: '/process' } as const
    expect(referenceOptions(names, mark, { process: 'approval' }, '/parameters/')).toEqual([{ name: 'Days' }])
    expect(referenceOptions(names, mark, { process: 'Gone' }, '/parameters/')).toEqual([])
  })

  it('lists the parameters and the columns of the data source the draft names', () => {
    const draft = {
      dataSource: 'orders',
      widgets: [{ dataSource: 'Orders', measure: 'openCount', parameters: { status: 'x' } }],
    }
    expect(referenceOptions(names, { part: 'parameter', of: '/dataSource' }, draft, '/parameters/status')).toEqual([
      { name: 'status' },
      { name: 'limit' },
    ])
    expect(referenceOptions(names, { part: 'column', of: '1/dataSource' }, draft, '/widgets/0/measure')).toEqual([
      { name: 'openCount' },
      { name: 'region' },
    ])
    expect(
      referenceOptions(names, { part: 'parameter', of: '2/dataSource' }, draft, '/widgets/0/parameters/status'),
    ).toEqual([{ name: 'status' }, { name: 'limit' }])
  })

  it('lists the operations of the connector the draft names', () => {
    const draft = { steps: [{ connector: { name: 'crm', operation: 'getAccount' } }] }
    expect(referenceOptions(names, { part: 'operation', of: '1/name' }, draft, '/steps/0/connector/operation')).toEqual(
      [{ name: 'getAccount' }, { name: 'listAccounts' }],
    )
  })

  it('lists the outcomes of the task steps with the name the draft gives, without repeats', () => {
    const other = {
      ...names,
      processes: [
        ...names.processes,
        { id: '6', name: 'Escalation', steps: [{ name: 'review', type: 'task', outcomes: ['Approve', 'Reject'] }] },
      ],
    } as StudioNames
    const draft = { steps: [{ type: 'completeTask', step: 'Review', outcome: 'Approve' }] }
    expect(referenceOptions(other, { part: 'outcome', of: '1/step' }, draft, '/steps/0/outcome')).toEqual([
      { name: 'Approve' },
      { name: 'Reject' },
    ])
    expect(referenceOptions(names, { part: 'outcome', of: '1/step' }, { steps: [{}] }, '/steps/0/outcome')).toEqual([])
  })

  it('finds the type of a data source parameter', () => {
    const draft = { dataSource: 'Orders' }
    const mark = { part: 'parameter', of: '/dataSource' } as const
    expect(parameterType(names, mark, draft, '/parameters/Limit', 'Limit')).toBe('integer')
    expect(parameterType(names, mark, draft, '/parameters/gone', 'gone')).toBeUndefined()
    expect(parameterType(names, mark, {}, '/parameters/limit', 'limit')).toBeUndefined()
  })

  it('finds the type of a process parameter', () => {
    const draft = { process: 'Approval' }
    const mark = { part: 'processParameter', of: '/process' } as const
    expect(parameterType(names, mark, draft, '/parameters/days', 'days')).toBe('integer')
    expect(parameterType(names, mark, draft, '/parameters/gone', 'gone')).toBeUndefined()
  })

  it('lists nothing when the owner is not named', () => {
    expect(
      referenceOptions(names, { part: 'field', of: '2/entity' }, { grants: [{ fields: [] }] }, '/grants/0/fields/0'),
    ).toEqual([])
  })
})
