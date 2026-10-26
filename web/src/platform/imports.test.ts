import { describe, expect, it } from 'vitest'
import { importContentType, importErrorRows, importsPath } from './imports'

describe('importsPath', () => {
  it('swaps the last records segment for imports', () => {
    expect(importsPath('/api/apps/Records/entities/Item/records')).toBe('/api/apps/Records/entities/Item/imports')
    expect(importsPath('/api/apps/records/entities/records/records')).toBe('/api/apps/records/entities/records/imports')
  })
})

describe('importContentType', () => {
  it.each([
    ['assets.csv', 'text/csv'],
    ['ASSETS.CSV', 'text/csv'],
    ['assets.xlsx', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'],
    ['Assets.XLSX', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'],
  ])('maps %s', (name, type) => {
    expect(importContentType({ name })).toBe(type)
  })

  it.each(['assets.xls', 'assets.txt', 'csv'])('refuses %s', (name) => {
    expect(importContentType({ name })).toBeNull()
  })
})

describe('importErrorRows', () => {
  it('parses each key kind and keeps the server order', () => {
    expect(
      importErrorRows({
        '/rows/2/values/purchaseCost': ['Must be positive.', 'Too small.'],
        '/rows/3': ['Wrong number of cells.'],
        '/columns/4': ['Unknown field.'],
        '/columns': ['No header.'],
        '/rows': ['Too many rows.'],
        '': ['Gone.'],
      }),
    ).toEqual([
      { kind: 'field', row: 2, field: 'purchaseCost', message: 'Must be positive.' },
      { kind: 'field', row: 2, field: 'purchaseCost', message: 'Too small.' },
      { kind: 'row', row: 3, message: 'Wrong number of cells.' },
      { kind: 'column', column: 4, message: 'Unknown field.' },
      { kind: 'file', message: 'No header.' },
      { kind: 'file', message: 'Too many rows.' },
      { kind: 'file', message: 'Gone.' },
    ])
  })

  it('has no rows without errors', () => {
    expect(importErrorRows({})).toEqual([])
  })
})
