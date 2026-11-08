import { describe, expect, it } from 'vitest'
import type { FieldMetadata } from './site'
import type { FilterGroup } from './tableFilter'
import {
  columnsChoice,
  parseTableQuery,
  recordQuery,
  shownColumns,
  tablePageSizes,
  writeTableQuery,
} from './tableQuery'

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

describe('search', () => {
  it('is read, as typed and without trimming, only when the table is searchable', () => {
    const params = new URLSearchParams('search=%20paper%20&search=other')

    expect(parseTableQuery(params, fields, 20, [], true).search).toBe(' paper ')
    expect(parseTableQuery(params, fields).search).toBeUndefined()
    expect(parseTableQuery(new URLSearchParams('search='), fields, 20, [], true).search).toBeUndefined()
  })

  it('is written after the parameter values and before page, and removed when cleared', () => {
    const query = { page: 2, pageSize: 20, sort: null, parameters: { minTotal: '5' }, search: 'paper' }

    expect(writeTableQuery(new URLSearchParams('x=1&search=old'), query, 20, ['minTotal']).toString()).toBe(
      'x=1&minTotal=5&search=paper&page=2',
    )
    expect(writeTableQuery(new URLSearchParams('search=old'), { page: 1, pageSize: 20, sort: null }).toString()).toBe(
      '',
    )
  })

  it('is sent in the request', () => {
    expect(recordQuery({ page: 1, pageSize: 20, sort: null, search: 'a&b' })).toBe('search=a%26b')
  })
})

describe('filter', () => {
  const filterFields = [
    { name: 'title', type: 'text' },
    { name: 'total', type: 'integer' },
  ] as FieldMetadata[]
  const parse = (filter: string) =>
    parseTableQuery(new URLSearchParams({ filter }), filterFields, 20, [], false, true).filter

  it('drops a filter that is not valid and writes a valid one after search and before page', () => {
    for (const filter of [
      'not json',
      '{"all":[{"field":"nope","op":"eq","value":"x"}]}',
      '{"all":[{"field":"title","op":"empty","value":"x"}]}',
      '{"all":[{"field":"title","op":"like","value":"x"}]}',
      '{"all":[{"field":"title","op":"eq","value":null}]}',
      '{"all":[],"any":[]}',
      '{"field":"title","op":"eq","value":"x"}',
    ]) {
      expect(parse(filter)).toBeUndefined()
    }
    expect(
      parseTableQuery(new URLSearchParams('filter={"all":[{"field":"title","op":"eq","value":"x"}]}'), filterFields),
    ).not.toHaveProperty('filter')

    const filter: FilterGroup = {
      any: [{ field: 'title', op: 'contains', value: 'a b' }, { all: [{ field: 'total', op: 'empty' }] }],
    }
    expect(parse(JSON.stringify(filter))).toEqual(filter)

    const query = { page: 2, pageSize: 20, sort: null, search: 'x', filter }
    const written = writeTableQuery(new URLSearchParams('filter=old&other=1'), query).toString()
    expect(written).toBe(`other=1&search=x&${new URLSearchParams({ filter: JSON.stringify(filter) })}&page=2`)
    expect(writeTableQuery(new URLSearchParams('filter=old'), { page: 1, pageSize: 20, sort: null }).toString()).toBe(
      '',
    )
  })
})

describe('columns', () => {
  const columnFields = [
    { name: 'title', type: 'text' },
    { name: 'total', type: 'integer' },
    { name: 'note', type: 'text' },
  ] as FieldMetadata[]
  const keys = ['title', 'total', 'note']
  const parse = (columns: string, control = true) =>
    parseTableQuery(new URLSearchParams({ columns }), columnFields, 20, [], false, false, control).columns

  it('shows the first key first, then the known keys of the choice once each', () => {
    expect(shownColumns(keys, ['note', 'title', 'nope', 'note'])).toEqual(['title', 'note'])
    expect(shownColumns(keys, ['note', 'total'])).toEqual(['title', 'note', 'total'])
    expect(shownColumns(keys, ['title'])).toEqual(['title'])
  })

  it('shows every key when there is no choice or none of it is known', () => {
    expect(shownColumns(keys)).toEqual(keys)
    expect(shownColumns(keys, [])).toEqual(keys)
    expect(shownColumns(keys, ['nope'])).toEqual(keys)
  })

  it('keeps no choice when every key shows in its first order', () => {
    expect(columnsChoice(keys, keys)).toBeUndefined()
    expect(columnsChoice(keys, ['title', 'note', 'total'])).toEqual(['title', 'note', 'total'])
    expect(columnsChoice(keys, ['title', 'total'])).toEqual(['title', 'total'])
  })

  it('is read with the first column first and unknown or repeated keys dropped, only when the table has the control', () => {
    expect(parse('note,title,nope,note')).toEqual(['title', 'note'])
    expect(parse('note,total')).toEqual(['title', 'note', 'total'])
    expect(parse('total,note')).toBeUndefined()
    expect(parse('title,total,note')).toBeUndefined()
    expect(parse('nope')).toBeUndefined()
    expect(parse('note,title', false)).toBeUndefined()
    expect(
      parseTableQuery(new URLSearchParams('columns=note'), columnFields, 20, [], false, false, false),
    ).not.toHaveProperty('columns')
  })

  it('is written after sort, replaces the old value and goes when it is undefined', () => {
    const query = { page: 2, pageSize: 50, sort: { field: 'title', descending: true }, columns: ['title', 'note'] }

    expect(writeTableQuery(new URLSearchParams('x=1&columns=old'), query).toString()).toBe(
      'x=1&page=2&pageSize=50&sort=-title&columns=title%2Cnote',
    )
    expect(writeTableQuery(new URLSearchParams('columns=old'), { page: 1, pageSize: 20, sort: null }).toString()).toBe(
      '',
    )
  })

  it('is never sent in the request', () => {
    expect(recordQuery({ page: 1, pageSize: 20, sort: null, columns: ['title', 'note'] })).toBe('')
  })
})
