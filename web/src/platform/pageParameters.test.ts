import { describe, expect, it } from 'vitest'
import { readPageParameters } from './pageParameters'
import type { FieldType, PageParameter } from './site'

function parameter(type: FieldType, overrides: Partial<PageParameter> = {}): PageParameter {
  return { name: 'p', type, required: true, values: null, target: null, ...overrides }
}

const read = (query: string, ...parameters: PageParameter[]) =>
  readPageParameters(new URLSearchParams(query), parameters)

describe('readPageParameters', () => {
  it('returns the values the URL gives', () => {
    const category = parameter('reference', { name: 'category' })
    const note = parameter('text', { name: 'note', required: false })

    expect(read('note=hello&category=0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01&page=2', category, note)).toEqual({
      category: '0d3a1c52-2f0b-4b1e-9a51-6c0f7a1d2e01',
      note: 'hello',
    })
  })

  it('returns no values for a page without parameters', () => {
    expect(read('page=2')).toEqual({})
  })

  it.each(['', 'p=', 'p=1&p=2', 'p=1&p=1'])(
    'is not found when the required value in "%s" is missing or repeated',
    (query) => {
      expect(read(query, parameter('integer'))).toBeNull()
    },
  )

  it.each([
    ['text', '\0'],
    ['integer', '1.5'],
    ['integer', '9223372036854775808'],
    ['integer', '٣'],
    ['decimal', '1e3'],
    ['decimal', '1.'],
    ['boolean', 'True'],
    ['date', '2026-02-30'],
    ['date', '2026-2-3'],
    ['date', '0000-01-01'],
    ['date-time', '2026-02-03T10:00:00'],
    ['date-time', '2026-02-03T24:00:00Z'],
    ['date-time', '2026-02-03T10:00:00+15:00'],
    ['enum', 'closed'],
    ['reference', 'nope'],
    ['reference', '0d3a1c522f0b4b1e9a516c0f7a1d2e01'],
  ] as const)('is not found when a required %s value is "%s"', (type, value) => {
    const query = new URLSearchParams({ p: value })

    expect(readPageParameters(query, [parameter(type, { values: ['open', 'done'] })])).toBeNull()
  })

  it.each([
    ['text', 'any text'],
    ['integer', '-9223372036854775808'],
    ['integer', '9223372036854775807'],
    ['decimal', '-1250.50'],
    ['boolean', 'false'],
    ['date', '2024-02-29'],
    ['date-time', '2026-02-03T10:00:00.123456+07:00'],
    ['date-time', '2026-02-03t10:00:00z'],
    ['enum', 'done'],
    ['reference', '0D3A1C52-2F0B-4B1E-9A51-6C0F7A1D2E01'],
  ] as const)('accepts a %s value of "%s"', (type, value) => {
    const query = new URLSearchParams({ p: value })

    expect(readPageParameters(query, [parameter(type, { values: ['open', 'done'] })])).toEqual({ p: value })
  })

  it.each(['', 'p=', 'p=x', 'p=1&p=2'])('drops an optional value in "%s" that is missing or malformed', (query) => {
    expect(read(query, parameter('integer', { required: false }))).toEqual({})
  })
})
