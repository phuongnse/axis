import { describe, expect, it } from 'vitest'
import type { FieldMetadata } from './site'
import { parseTableQuery, recordQuery, writeTableQuery } from './tableQuery'

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
    })
  })

  it('falls back to the defaults for invalid values', () => {
    for (const search of [
      'page=0&pageSize=7&sort=nope',
      'page=-1&pageSize=1000&sort=Title',
      'page=1.5&pageSize=10.0&sort=category',
      'page=+2&pageSize=%2010&sort=--title',
    ]) {
      expect(parseTableQuery(new URLSearchParams(search), fields)).toEqual({ page: 1, pageSize: 20, sort: null })
    }
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
