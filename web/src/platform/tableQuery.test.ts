import { describe, expect, it } from 'vitest'
import type { FieldMetadata } from './site'
import { parseTableQuery, recordQuery, tablePageSizes, writeTableQuery } from './tableQuery'

const fields = [
  { name: 'title', type: 'text' },
  { name: 'category', type: 'reference' },
] as FieldMetadata[]

describe('parseTableQuery', () => {
  it('reads page, pageSize and a descending sort', () => {
    expect(parseTableQuery(new URLSearchParams('page=3&pageSize=50&sort=-title'), fields)).toEqual({
      page: 3,
      pageSize: 50,
      sort: { field: 'title', descending: true },
      parameters: {},
    })
  })

  it('falls back to the defaults for invalid values', () => {
    for (const search of [
      'page=0&pageSize=7&sort=nope',
      'page=-1&pageSize=1000&sort=Title',
      'page=1.5&pageSize=10.0&sort=category',
      'page=+2&pageSize=%2010&sort=--title',
    ]) {
      expect(parseTableQuery(new URLSearchParams(search), fields)).toEqual({
        page: 1,
        pageSize: 20,
        sort: null,
        parameters: {},
      })
    }
  })
})

describe('data source parameters', () => {
  const names = ['statusFilter', 'minTotal']

  it('reads the values of the named parameters by exact name', () => {
    const query = parseTableQuery(
      new URLSearchParams('minTotal=10&statusfilter=draft&statusFilter=approved'),
      fields,
      20,
      names,
    )

    expect(query.parameters).toEqual({ statusFilter: 'approved', minTotal: '10' })
  })

  it('drops empty values and keeps only the first of repeated values', () => {
    expect(
      parseTableQuery(new URLSearchParams('statusFilter=&minTotal=5&minTotal=7'), fields, 20, names).parameters,
    ).toEqual({
      minTotal: '5',
    })
  })

  it('writes the values before page, pageSize and sort, after other parameters', () => {
    const current = new URLSearchParams('page=2&minTotal=5&x=1&statusFilter=draft&statusFilter=approved')
    const query = {
      page: 2,
      pageSize: 10,
      sort: { field: 'title', descending: true },
      parameters: { statusFilter: 'approved', minTotal: '5' },
    }

    expect(writeTableQuery(current, query, 20, names).toString()).toBe(
      'x=1&statusFilter=approved&minTotal=5&page=2&pageSize=10&sort=-title',
    )
  })

  it('removes a cleared value from the URL', () => {
    const current = new URLSearchParams('statusFilter=draft&minTotal=5')

    expect(
      writeTableQuery(
        current,
        { page: 1, pageSize: 20, sort: null, parameters: { minTotal: '5' } },
        20,
        names,
      ).toString(),
    ).toBe('minTotal=5')
  })

  it('sends the values first in the request and leaves out empty ones', () => {
    expect(
      recordQuery({ page: 3, pageSize: 20, sort: null, parameters: { statusFilter: 'approved', minTotal: '' } }),
    ).toBe('statusFilter=approved&page=3')
  })
})

describe('a table with its own default page size', () => {
  it('offers that size among the standard ones, in order', () => {
    expect(tablePageSizes()).toEqual([10, 20, 50, 100])
    expect(tablePageSizes(10)).toEqual([10, 20, 50, 100])
    expect(tablePageSizes(25)).toEqual([10, 20, 25, 50, 100])
  })

  it('reads, falls back to and leaves out that size as the default', () => {
    expect(parseTableQuery(new URLSearchParams('pageSize=25'), fields, 25).pageSize).toBe(25)
    expect(parseTableQuery(new URLSearchParams('pageSize=7'), fields, 25).pageSize).toBe(25)
    expect(recordQuery({ page: 1, pageSize: 25, sort: null }, 25)).toBe('')
    expect(writeTableQuery(new URLSearchParams(), { page: 1, pageSize: 20, sort: null }, 25).toString()).toBe(
      'pageSize=20',
    )
  })
})

describe('writeTableQuery', () => {
  it('keeps other parameters first and writes its own in a fixed order without defaults', () => {
    const current = new URLSearchParams('sort=title&x=1&pageSize=10&y=2')
    const query = { page: 2, pageSize: 10, sort: { field: 'title', descending: false } }

    expect(writeTableQuery(current, query).toString()).toBe('x=1&y=2&page=2&pageSize=10&sort=title')
    expect(writeTableQuery(current, { page: 1, pageSize: 20, sort: null }).toString()).toBe('x=1&y=2')
  })
})

describe('recordQuery', () => {
  it('serialises only the parameters that differ from the defaults', () => {
    expect(recordQuery({ page: 1, pageSize: 20, sort: null })).toBe('')
    expect(recordQuery({ page: 4, pageSize: 100, sort: { field: 'title', descending: true } })).toBe(
      'page=4&pageSize=100&sort=-title',
    )
  })
})
