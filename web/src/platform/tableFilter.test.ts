import { describe, expect, it } from 'vitest'
import {
  countConditions,
  matchesFilter,
  parseFilter,
  pruneFilter,
  type FilterGroup,
  type FilterReader,
} from './tableFilter'

interface Row {
  name: string | null
  amount: number | null
  due: string | null
}

const readers: Record<string, FilterReader<Row>> = {
  name: { kind: 'text', value: (row) => row.name },
  amount: { kind: 'number', value: (row) => row.amount },
  due: { kind: 'date', value: (row) => row.due },
}
const match = (row: Row, filter: FilterGroup) => matchesFilter(row, filter, (field) => readers[field], 'en')
const row = (name: string | null, amount: number | null = null, due: string | null = null): Row => ({
  name,
  amount,
  due,
})

describe('pruneFilter', () => {
  it('leaves out a condition with no value and a group left empty, but keeps empty and notEmpty', () => {
    const pruned = pruneFilter({
      all: [
        { field: 'name', op: 'eq' },
        { field: 'name', op: 'contains', value: '' },
        { any: [{ field: 'amount', op: 'gt' }] },
        { field: 'amount', op: 'notEmpty' },
      ],
    })
    expect(pruned).toEqual({ all: [{ field: 'amount', op: 'notEmpty' }] })
    expect(countConditions(pruned)).toBe(1)
    expect(pruneFilter({ any: [{ field: 'name', op: 'eq' }] })).toBeUndefined()
  })
})

describe('parseFilter', () => {
  it('reads a nested filter and refuses a node of another shape', () => {
    const text = '{"any":[{"field":"name","op":"eq","value":"a"},{"all":[{"field":"amount","op":"empty"}]}]}'
    expect(parseFilter(text, ['name', 'amount'])).toEqual(JSON.parse(text))
    expect(parseFilter(text, ['name'])).toBeUndefined()
    expect(parseFilter('{"all":[{"field":"name","op":"eq","value":"a","extra":1}]}', ['name'])).toBeUndefined()
    expect(parseFilter('{"all":{}}', ['name'])).toBeUndefined()
    expect(parseFilter('{"all":[]}', ['name'])).toBeUndefined()
  })
})

describe('matchesFilter', () => {
  it('keeps an empty value on ne, and never matches it on other comparisons', () => {
    const ne: FilterGroup = { all: [{ field: 'name', op: 'ne', value: 'a' }] }
    expect(match(row(null), ne)).toBe(true)
    expect(match(row(''), ne)).toBe(true)
    expect(match(row('a'), ne)).toBe(false)
    expect(match(row('b'), ne)).toBe(true)
    expect(match(row(null, null), { all: [{ field: 'amount', op: 'lt', value: 5 }] })).toBe(false)
    expect(match(row(''), { all: [{ field: 'name', op: 'empty' }] })).toBe(true)
  })

  it('compares text without letter case on contains and exactly on eq, numbers by size and dates as text', () => {
    expect(match(row('Papaya'), { all: [{ field: 'name', op: 'contains', value: 'PAP' }] })).toBe(true)
    expect(match(row('Papaya'), { all: [{ field: 'name', op: 'eq', value: 'papaya' }] })).toBe(false)
    expect(match(row('x', 9), { all: [{ field: 'amount', op: 'lt', value: 10 }] })).toBe(true)
    expect(match(row('x', 10), { all: [{ field: 'amount', op: 'gte', value: 10 }] })).toBe(true)
    expect(match(row('x', null, '2026-02-01'), { all: [{ field: 'due', op: 'gt', value: '2026-01-31' }] })).toBe(true)
  })

  it('needs every node of an all group and one of an any group', () => {
    const filter: FilterGroup = {
      all: [
        { field: 'name', op: 'contains', value: 'a' },
        {
          any: [
            { field: 'amount', op: 'gt', value: 10 },
            { field: 'amount', op: 'empty' },
          ],
        },
      ],
    }
    expect(match(row('a', 11), filter)).toBe(true)
    expect(match(row('a', null), filter)).toBe(true)
    expect(match(row('a', 5), filter)).toBe(false)
    expect(match(row('b', 11), filter)).toBe(false)
  })
})
