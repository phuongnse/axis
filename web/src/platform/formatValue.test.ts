import { describe, expect, it } from 'vitest'
import { formatValue, type FormatOptions } from './formatValue'
import type { FieldMetadata, FieldType } from './site'

function field(name: string, type: FieldType, valueLabelKeys?: Record<string, string>): FieldMetadata {
  return {
    name,
    type,
    labelKey: null,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    min: null,
    max: null,
    minLength: null,
    values: null,
    valueLabelKeys,
    target: null,
    fields: null,
  }
}

const texts: Record<string, string> = {
  'shell.table.yes': 'Yes',
  'shell.table.no': 'No',
  'request.status.submitted': 'Đã gửi',
}
const options: FormatOptions = { locale: 'en', timeZone: 'UTC', text: (key) => texts[key] ?? key }

describe('formatValue', () => {
  it('shows a date-time to the second without the fraction', () => {
    // ICU may put a narrow no-break space before AM.
    expect(formatValue(field('dueAt', 'date-time'), '2026-10-06T02:00:00.123456Z', {}, options)).toMatch(
      /^Oct 6, 2026, 2:00:00\sAM$/,
    )
  })

  it('shows a date in the locale without shifting the day', () => {
    expect(formatValue(field('due', 'date'), '2026-10-06', {}, { ...options, timeZone: 'Pacific/Kiritimati' })).toBe(
      'Oct 6, 2026',
    )
    expect(formatValue(field('due', 'date'), '2026-10-06', {}, { ...options, timeZone: 'Pacific/Pago_Pago' })).toBe(
      'Oct 6, 2026',
    )
  })

  it('shows a reference as its label', () => {
    const id = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'
    expect(formatValue(field('category', 'reference'), id, { category: 'Finance' }, options)).toBe('Finance')
  })

  it('shows the id of a reference whose label is not a string', () => {
    const id = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'
    expect(formatValue(field('lines', 'reference'), id, { lines: [{ lines: 'Desk' }] }, options)).toBe(id)
  })

  it('shows a reference of a child row as the label of that row', () => {
    const id = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'
    const rowLabels = { product: 'Desk' }
    expect(formatValue(field('product', 'reference'), id, rowLabels, options)).toBe('Desk')
    expect(formatValue(field('product', 'reference'), id, {}, options)).toBe(id)
  })

  it('shows an enum with a label key as its translated text', () => {
    const status = field('status', 'enum', { submitted: 'request.status.submitted' })
    expect(formatValue(status, 'submitted', {}, options)).toBe('Đã gửi')
  })

  it('shows an enum without a label key as the raw value', () => {
    expect(formatValue(field('status', 'enum'), 'submitted', {}, options)).toBe('submitted')
    expect(formatValue(field('status', 'enum', null as never), 'submitted', {}, options)).toBe('submitted')
    const status = field('status', 'enum', { submitted: 'request.status.submitted' })
    expect(formatValue(status, 'draft', {}, options)).toBe('draft')
  })

  it('shows a boolean as the localized yes or no', () => {
    expect(formatValue(field('done', 'boolean'), true, {}, options)).toBe('Yes')
    expect(formatValue(field('done', 'boolean'), false, {}, options)).toBe('No')
  })

  it('keeps numbers exactly as the API wrote them', () => {
    expect(formatValue(field('price', 'decimal'), '1250.50', {}, options)).toBe('1250.50')
    expect(formatValue(field('count', 'integer'), '12345678901234567890', {}, options)).toBe('12345678901234567890')
  })

  it('shows null as an empty cell', () => {
    expect(formatValue(field('title', 'text'), null, {}, options)).toBe('')
    expect(formatValue(field('done', 'boolean'), null, {}, options)).toBe('')
  })
})
