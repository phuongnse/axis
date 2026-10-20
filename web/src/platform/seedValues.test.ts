import { describe, expect, it } from 'vitest'
import { parseNumberText, seedColumns, seedProblems, seedValueProblem, type SeedField } from './seedValues'

const required = 'shell.studio.seed.required'
const wrongType = 'shell.studio.seed.wrongType'
const outOfBounds = 'shell.studio.seed.outOfBounds'
const invalidDate = 'shell.studio.seed.invalidDate'

function problem(field: Partial<SeedField> & { type: string }, value: unknown) {
  return seedValueProblem({ name: 'x', ...field }, value)
}

describe('seedColumns', () => {
  it('drops child collections, computed, sequence and creator fields', () => {
    const fields: SeedField[] = [
      { name: 'code', type: 'text' },
      { name: 'lines', type: 'child-collection' },
      { name: 'total', type: 'decimal', expression: 'a + b' },
      { name: 'number', type: 'text', sequence: 'Numbers' },
      { name: 'owner', type: 'text', creator: true },
      { name: 'other', type: 'text', creator: false },
    ]

    expect(seedColumns(fields).map((field) => field.name)).toEqual(['code', 'other'])
  })
})

describe('seedValueProblem', () => {
  it('accepts a missing or null value of an optional field, and flags a required one', () => {
    expect(problem({ type: 'text' }, undefined)).toBeNull()
    expect(problem({ type: 'integer' }, null)).toBeNull()
    expect(problem({ type: 'text', required: true }, undefined)).toBe(required)
    expect(problem({ type: 'text', required: true }, null)).toBe(required)
  })

  it('checks text for its type, U+0000 and its length in code points', () => {
    expect(problem({ type: 'text', maxLength: 3 }, 'abc')).toBeNull()
    expect(problem({ type: 'text', maxLength: 3 }, 'abcd')).toBe('shell.studio.seed.tooLong')
    // One code point outside the BMP counts as one.
    expect(problem({ type: 'text', maxLength: 1 }, '😀')).toBeNull()
    expect(problem({ type: 'text' }, 'a\0b')).toBe(wrongType)
    expect(problem({ type: 'text' }, 5)).toBe(wrongType)
  })

  it('never coerces between JSON types', () => {
    expect(problem({ type: 'integer' }, '5')).toBe(wrongType)
    expect(problem({ type: 'decimal' }, '1.5')).toBe(wrongType)
    expect(problem({ type: 'boolean' }, 'true')).toBe(wrongType)
    expect(problem({ type: 'text' }, true)).toBe(wrongType)
  })

  it('checks integers for an integral value within 64 bits', () => {
    expect(problem({ type: 'integer' }, 5)).toBeNull()
    expect(problem({ type: 'integer' }, -3)).toBeNull()
    expect(problem({ type: 'integer' }, 5.5)).toBe(wrongType)
    expect(problem({ type: 'integer' }, 2 ** 63)).toBe(outOfBounds)
    expect(problem({ type: 'integer' }, -(2 ** 63))).toBeNull()
  })

  it('checks decimals against precision and scale after the exponent', () => {
    const money = { type: 'decimal', precision: 5, scale: 2 }
    expect(problem(money, 123.45)).toBeNull()
    expect(problem(money, 1.5)).toBeNull()
    expect(problem(money, 1234.5)).toBe(outOfBounds)
    expect(problem(money, 1.234)).toBe(outOfBounds)
    expect(problem(money, 1.5e2)).toBeNull()
    expect(problem(money, 1e-7)).toBe(outOfBounds)
    expect(problem(money, 1e21)).toBe(outOfBounds)
    expect(problem({ type: 'decimal', precision: 3 }, 100)).toBeNull()
    expect(problem({ type: 'decimal', precision: 3 }, 0.5)).toBe(outOfBounds)
    expect(problem({ type: 'decimal' }, 0.1234567)).toBeNull()
  })

  it('checks booleans', () => {
    expect(problem({ type: 'boolean' }, false)).toBeNull()
    expect(problem({ type: 'boolean' }, 0)).toBe(wrongType)
  })

  it('checks dates as real calendar dates', () => {
    expect(problem({ type: 'date' }, '2024-02-29')).toBeNull()
    expect(problem({ type: 'date' }, '2023-02-29')).toBe(invalidDate)
    expect(problem({ type: 'date' }, '2023-13-01')).toBe(invalidDate)
    expect(problem({ type: 'date' }, '0000-01-01')).toBe(invalidDate)
    expect(problem({ type: 'date' }, '2023-1-01')).toBe(invalidDate)
    expect(problem({ type: 'date' }, 20230101)).toBe(wrongType)
  })

  it('checks date-times for an RFC 3339 form with an offset', () => {
    expect(problem({ type: 'date-time' }, '2026-10-20T12:30:00Z')).toBeNull()
    expect(problem({ type: 'date-time' }, '2026-10-20t12:30:00.123456z')).toBeNull()
    expect(problem({ type: 'date-time' }, '2026-10-20T12:30:00+07:00')).toBeNull()
    expect(problem({ type: 'date-time' }, '2026-10-20T12:30:00+14:00')).toBeNull()
    expect(problem({ type: 'date-time' }, '2026-10-20T12:30:00+14:30')).toBe(invalidDate)
    expect(problem({ type: 'date-time' }, '2026-10-20T12:30:00')).toBe(invalidDate)
    expect(problem({ type: 'date-time' }, '2026-10-20T24:00:00Z')).toBe(invalidDate)
    expect(problem({ type: 'date-time' }, '2026-10-20T12:30:60Z')).toBe(invalidDate)
    expect(problem({ type: 'date-time' }, '2026-10-20T12:30:00.1234567Z')).toBe(invalidDate)
    expect(problem({ type: 'date-time' }, '2026-02-30T12:30:00Z')).toBe(invalidDate)
  })

  it('checks enums against the values', () => {
    const status = { type: 'enum', values: ['Open', 'Closed'] }
    expect(problem(status, 'Open')).toBeNull()
    expect(problem(status, 'open')).toBe('shell.studio.seed.notInValues')
    expect(problem(status, 1)).toBe(wrongType)
  })

  it('checks references for the hyphenated UUID form', () => {
    expect(problem({ type: 'reference' }, 'C6A80CFD-75d2-4847-8cc0-f603c2af74fa')).toBeNull()
    expect(problem({ type: 'reference' }, 'c6a80cfd75d248478cc0f603c2af74fa')).toBe(wrongType)
    expect(problem({ type: 'reference' }, 'dung')).toBe(wrongType)
  })
})

describe('seedProblems', () => {
  const columns: SeedField[] = [
    { name: 'code', type: 'text', required: true },
    { name: 'size', type: 'integer' },
  ]

  it('keys each problem by the pointer of its value and ignores fields that are not columns', () => {
    const records = [
      { id: 'a', values: { code: 'A', size: 1, unknown: 5, lines: [{}] } },
      { id: 'b', values: { size: '5' } },
      { id: 'c' },
    ]

    expect([...seedProblems(records, columns)]).toEqual([
      ['/records/1/values/code', required],
      ['/records/1/values/size', wrongType],
      ['/records/2/values/code', required],
    ])
  })

  it('has no problem when the records are not a list', () => {
    expect(seedProblems(undefined, columns).size).toBe(0)
    expect(seedProblems({}, columns).size).toBe(0)
  })
})

describe('parseNumberText', () => {
  it('gives a number for valid text and the text itself otherwise', () => {
    expect(parseNumberText('42', 'integer')).toBe(42)
    expect(parseNumberText('-7', 'integer')).toBe(-7)
    expect(parseNumberText('abc', 'integer')).toBe('abc')
    expect(parseNumberText('1.5', 'integer')).toBe('1.5')
    expect(parseNumberText('1.50', 'decimal')).toBe(1.5)
    expect(parseNumberText('1.', 'decimal')).toBe('1.')
    expect(parseNumberText('1e3', 'decimal')).toBe('1e3')
  })
})
