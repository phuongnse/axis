import { describe, expect, it } from 'vitest'
import { exportAddress, exportPath } from './exports'
import type { WidgetMetadata } from './site'

const entityTable = { entity: { recordsPath: '/api/apps/Records/entities/Item/records' }, dataSource: null }
const dataSourceTable = (rowsPath: string) => ({ entity: null, dataSource: { rowsPath } })

describe('exportPath', () => {
  it('adds export to the records route of an entity table', () => {
    expect(exportPath(entityTable as unknown as WidgetMetadata)).toBe('/api/apps/Records/entities/Item/records/export')
  })

  it('swaps the rows segment of a data source table for export', () => {
    expect(exportPath(dataSourceTable('/api/apps/Records/data-sources/ItemsByDepartment/rows') as WidgetMetadata)).toBe(
      '/api/apps/Records/data-sources/ItemsByDepartment/export',
    )
  })

  it('keeps the module segment of a module data source', () => {
    expect(
      exportPath(dataSourceTable('/api/apps/Records/data-sources/departments/Departments/rows') as WidgetMetadata),
    ).toBe('/api/apps/Records/data-sources/departments/Departments/export')
  })
})

describe('exportAddress', () => {
  const path = '/api/apps/Records/entities/Item/export'

  it('holds only the format for a table with no search, sort or parameters', () => {
    expect(exportAddress(path, { page: 1, pageSize: 20, sort: null }, {}, 'xlsx', 20)).toBe(`${path}?format=xlsx`)
  })

  it('holds the parameter values, search, sort and format, and no page or pageSize, on page 3 of a larger page size', () => {
    const address = exportAddress(
      '/api/apps/Records/data-sources/ItemsByDepartment/export',
      {
        page: 3,
        pageSize: 50,
        sort: { field: 'departmentName', descending: true },
        parameters: { nameFilter: 'ignored' },
        search: 'pa per',
      },
      { nameFilter: 'Paper', department: 'Sales' },
      'csv',
      10,
    )

    const [base, query] = address.split('?')
    expect(base).toBe('/api/apps/Records/data-sources/ItemsByDepartment/export')
    expect([...new URLSearchParams(query)]).toEqual([
      ['nameFilter', 'Paper'],
      ['department', 'Sales'],
      ['search', 'pa per'],
      ['sort', '-departmentName'],
      ['format', 'csv'],
    ])
  })
})
