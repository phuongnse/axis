import { describe, expect, it } from 'vitest'
import { filterResources, groupByKind, type StudioResourceItem } from './studio'

function resource(id: string, kind: string, name: string | null, file: string): StudioResourceItem {
  return { id, kind, name, file, version: 'v' }
}

const items = [
  resource('aaaa0001-0000-4000-8000-000000000000', 'entity', 'PurchaseRequest', 'entities/purchase-request.json'),
  resource('bbbb0002-0000-4000-8000-000000000000', 'form', 'PurchaseRequestForm', 'forms/purchase-request.json'),
  resource('cccc0003-0000-4000-8000-000000000000', 'entity', 'Department', 'entities/department.json'),
  resource('dddd0004-0000-4000-8000-000000000000', 'application', null, 'application.json'),
]

describe('filterResources', () => {
  it('keeps every resource for an empty or blank search', () => {
    expect(filterResources(items, '')).toEqual(items)
    expect(filterResources(items, '   ')).toEqual(items)
  })

  it('matches part of a name in any letter case', () => {
    expect(filterResources(items, '  purchaserequest').map((item) => item.name)).toEqual([
      'PurchaseRequest',
      'PurchaseRequestForm',
    ])
    expect(filterResources(items, 'DEPART').map((item) => item.name)).toEqual(['Department'])
  })

  it('matches part of an id in any letter case', () => {
    expect(filterResources(items, 'BBBB0002').map((item) => item.name)).toEqual(['PurchaseRequestForm'])
  })

  it('does not match the file or the kind', () => {
    expect(filterResources(items, 'entities/')).toEqual([])
    expect(filterResources(items, 'form')).toEqual([items[1]])
  })
})

describe('groupByKind', () => {
  it('makes one group per kind, sorted by kind, with the items sorted by name or file', () => {
    const groups = groupByKind(items)

    expect(groups.map((group) => group.kind)).toEqual(['application', 'entity', 'form'])
    expect(groups[1].items.map((item) => item.name)).toEqual(['Department', 'PurchaseRequest'])
  })

  it('sorts an item without a name by its file', () => {
    const groups = groupByKind([
      resource('1', 'entity', null, 'entities/b.json'),
      resource('2', 'entity', 'zeta', 'entities/a.json'),
      resource('3', 'entity', null, 'entities/a.json'),
    ])

    expect(groups[0].items.map((item) => item.id)).toEqual(['3', '1', '2'])
  })

  it('returns no group for no resources', () => {
    expect(groupByKind([])).toEqual([])
  })
})
