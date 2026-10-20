import { describe, expect, it } from 'vitest'
import type { StudioNames } from './studio'
import { getAt, referenceOptions, referencePointer } from './studioNames'

const names = {
  locales: ['en'],
  dataSources: [],
  rules: [],
  connectors: [],
  events: [],
  sequences: [],
  forms: [],
  pages: [],
  entities: [
    {
      id: '1',
      name: 'Order',
      module: null,
      fields: [{ name: 'Total', type: 'decimal', target: null, values: null }],
    },
  ],
  processes: [
    {
      id: '2',
      name: 'Approval',
      steps: [
        { name: 'Review', type: 'task', outcomes: ['Approve'] },
        { name: 'Done', type: 'end', outcomes: [] },
      ],
    },
  ],
  roles: [{ id: '3', name: 'manager' }],
  texts: [{ key: 'orders.title', texts: { en: 'Orders' }, usedBy: 1 }],
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
    expect(referenceOptions(names, 'entity', {}, '/entity')).toEqual(['Order'])
    expect(referenceOptions(names, 'role', {}, '/roles/0')).toEqual(['manager'])
    expect(referenceOptions(names, 'text', {}, '/title/textKey')).toEqual(['orders.title'])
    expect(referenceOptions(names, 'page', {}, '/page')).toEqual([])
  })

  it('lists the fields of the entity the draft names', () => {
    const draft = { entity: 'order', grants: [{ entity: 'Order', fields: ['Total'] }] }
    expect(referenceOptions(names, { part: 'field', of: '2/entity' }, draft, '/grants/0/fields/0')).toEqual(['Total'])
    expect(referenceOptions(names, { part: 'field', of: '/entity' }, draft, '/fields/0')).toEqual(['Total'])
  })

  it('lists the steps of the process the draft names', () => {
    const draft = { name: 'Approval', steps: [{ name: 'Review', next: 'Done' }] }
    expect(referenceOptions(names, { part: 'step', of: '/name' }, draft, '/steps/0/next')).toEqual(['Review', 'Done'])
  })

  it('lists nothing when the owner is not named', () => {
    expect(
      referenceOptions(names, { part: 'field', of: '2/entity' }, { grants: [{ fields: [] }] }, '/grants/0/fields/0'),
    ).toEqual([])
  })
})
