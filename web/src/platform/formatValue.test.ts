import { describe, expect, it } from 'vitest'
import { datePattern, formatValue, referencesLabels, timePattern, type FormatOptions } from './formatValue'
import type { FieldMetadata, FieldType } from './site'

function field(name: string, type: FieldType, valueLabelKeys?: Record<string, string>): FieldMetadata {
  return {
    currency: null,
    currencyField: null,
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
  const vi: FormatOptions = { ...options, locale: 'vi' }

  it('shows a date-time to the minute on the locale clock, without seconds or the fraction', () => {
    const dueAt = field('dueAt', 'date-time')
    expect(formatValue(dueAt, '2026-03-04T16:30:45.123456Z', {}, options)).toBe('03/04/2026 4:30 PM')
    expect(formatValue(dueAt, '2026-03-04T16:30:45.123456Z', {}, vi)).toBe('04/03/2026 16:30')
  })

  it('shows a morning hour without a leading zero on a 12-hour clock and with one on a 24-hour clock', () => {
    const dueAt = field('dueAt', 'date-time')
    expect(formatValue(dueAt, '2026-03-04T09:05:00Z', {}, options)).toBe('03/04/2026 9:05 AM')
    expect(formatValue(dueAt, '2026-03-04T09:05:00Z', {}, vi)).toBe('04/03/2026 09:05')
  })

  it('shows a date-time in the given time zone', () => {
    const dueAt = field('dueAt', 'date-time')
    expect(formatValue(dueAt, '2026-03-04T16:30:00Z', {}, { ...vi, timeZone: 'Asia/Ho_Chi_Minh' })).toBe(
      '04/03/2026 23:30',
    )
  })

  it('writes plain spaces, never no-break spaces', () => {
    expect(formatValue(field('dueAt', 'date-time'), '2026-03-04T16:30:00Z', {}, options)).not.toMatch(/[\u00a0\u202f]/)
  })

  it('shows a date as the short numeric date of the locale without shifting the day', () => {
    const due = field('due', 'date')
    expect(formatValue(due, '2026-03-18', {}, options)).toBe('03/18/2026')
    expect(formatValue(due, '2026-03-18', {}, vi)).toBe('18/03/2026')
    expect(formatValue(due, '2026-10-06', {}, { ...options, timeZone: 'Pacific/Kiritimati' })).toBe('10/06/2026')
    expect(formatValue(due, '2026-10-06', {}, { ...options, timeZone: 'Pacific/Pago_Pago' })).toBe('10/06/2026')
  })

  it('shows a file as its name', () => {
    const stored = {
      id: '3c1d5e7f-9a2b-4c4d-8e6f-0a1b2c3d4e5f',
      name: 'paper.pdf',
      mediaType: 'application/pdf',
      size: '2048',
    }
    expect(formatValue(field('receipt', 'file'), stored, {}, options)).toBe('paper.pdf')
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

  it('groups integers and decimals by the locale and loses no digit', () => {
    const price = field('price', 'decimal')
    expect(formatValue(price, '16945.75', {}, options)).toBe('16,945.75')
    expect(formatValue(price, '16945.75', {}, vi)).toBe('16.945,75')
    expect(formatValue(price, '1250.50', {}, options)).toBe('1,250.50')
    expect(formatValue(field('count', 'integer'), '12345678901234567890', {}, options)).toBe(
      '12,345,678,901,234,567,890',
    )
    expect(formatValue(field('count', 'integer'), '1250000', {}, vi)).toBe('1.250.000')
  })

  it('pads a decimal to its scale and never rounds away a written digit', () => {
    const price = { ...field('price', 'decimal'), scale: 2 }
    expect(formatValue(price, '1250.5', {}, options)).toBe('1,250.50')
    expect(formatValue(price, '0.123456', {}, options)).toBe('0.123456')
  })

  it('leaves text that is not a number unchanged', () => {
    expect(formatValue(field('price', 'decimal'), 'abc', {}, options)).toBe('abc')
  })

  it('shows null as an empty cell', () => {
    expect(formatValue(field('title', 'text'), null, {}, options)).toBe('')
    expect(formatValue(field('done', 'boolean'), null, {}, options)).toBe('')
  })

  describe('money', () => {
    // ICU puts no-break spaces before some currency signs, so spaces are matched loosely.
    const amount = (currency: string, scale: number) => ({ ...field('amount', 'money'), currency, scale })
    const vi: FormatOptions = { ...options, locale: 'vi' }

    it('shows a fixed currency at the field scale in the UI locale', () => {
      expect(formatValue(amount('USD', 2), '1250.50', {}, options)).toBe('$1,250.50')
      expect(formatValue(amount('VND', 0), '1250000', {}, options)).toMatch(/^₫1,250,000$/)
      expect(formatValue(amount('USD', 2), '1250.50', {}, vi)).toMatch(/^1\.250,50\sUS\$$/)
      expect(formatValue(amount('VND', 0), '1250000', {}, vi)).toMatch(/^1\.250\.000\s₫$/)
    })

    it('pads to the scale and never rounds away a written digit', () => {
      expect(formatValue(amount('USD', 2), '1250.5', {}, options)).toBe('$1,250.50')
      expect(formatValue(amount('VND', 0), '1250.50', {}, options)).toMatch(/^₫1,250\.50$/)
      expect(formatValue(amount('USD', 2), '12345678901234567.89', {}, options)).toBe('$12,345,678,901,234,567.89')
    })

    it('reads the currency from the row when the field names a currency field', () => {
      const money = { ...field('amount', 'money'), scale: 2, currencyField: 'currency' }
      expect(formatValue(money, '1250.50', {}, options, { currency: 'USD', amount: '1250.50' })).toBe('$1,250.50')
      expect(formatValue(money, '1250.50', {}, options, { currency: 'EUR' })).toMatch(/^€1,250\.50$/)
    })

    it('reads the currency from the currency column of a data source column', () => {
      const column = {
        name: 'amountSum',
        type: 'money' as const,
        labelKey: null,
        values: null,
        target: null,
        currencyColumn: 'currency',
      }
      expect(formatValue(column, '1250.50', {}, options, { currency: 'USD', amountSum: '1250.50' })).toBe('$1,250.50')
    })

    it('shows a grouped number without a usable currency', () => {
      const money = { ...field('amount', 'money'), scale: 2, currencyField: 'currency' }
      expect(formatValue(money, '1250.50', {}, options)).toBe('1,250.50')
      expect(formatValue(money, '1250.50', {}, vi)).toBe('1.250,50')
      expect(formatValue(money, '1250.50', {}, options, { currency: null })).toBe('1,250.50')
      expect(formatValue(money, '1250.50', {}, options, { currency: 'dollars' })).toBe('1,250.50')
      expect(formatValue(money, '1250.50', {}, options, { currency: true })).toBe('1,250.50')
      expect(formatValue(amount('US', 2), '1250.5', {}, options)).toBe('1,250.50')
      expect(formatValue(amount('USD', 2), 'abc', {}, options)).toBe('abc')
    })
  })
})

describe('datePattern', () => {
  it('is the pattern of the shown date, so a picker writes what the text shows', () => {
    expect(datePattern('en', false)).toBe('MM/DD/YYYY')
    expect(datePattern('vi', false)).toBe('DD/MM/YYYY')
  })

  it('adds the time to the minute on the locale clock for a date-time', () => {
    expect(datePattern('en', true)).toBe('MM/DD/YYYY h:mm A')
    expect(datePattern('vi', true)).toBe('DD/MM/YYYY HH:mm')
    expect(timePattern('en')).toBe('h:mm A')
    expect(timePattern('vi')).toBe('HH:mm')
  })
})

describe('referencesLabels', () => {
  it('returns the label of each id in order, and the id where its label is missing or null', () => {
    expect(referencesLabels(['a', 'b', 'c', 'd'], ['Word', null, 'Visio'])).toEqual(['Word', 'b', 'Visio', 'd'])
  })

  it('returns nothing for an empty or missing set', () => {
    expect(referencesLabels([], ['Word'])).toEqual([])
    expect(referencesLabels(undefined, undefined)).toEqual([])
    expect(referencesLabels(null, 'Word')).toEqual([])
  })
})
