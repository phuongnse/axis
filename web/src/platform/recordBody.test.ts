import { describe, expect, it } from 'vitest'
import { buildRecordBody, isJsonNumber } from './recordBody'
import type { FieldMetadata, FieldType } from './site'

function field(name: string, type: FieldType): FieldMetadata {
  return {
    name,
    type,
    labelKey: null,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    maxLength: null,
    precision: null,
    scale: null,
    values: null,
    target: null,
    fields: null,
  }
}

const fields = [
  field('title', 'text'),
  field('priority', 'integer'),
  field('amount', 'decimal'),
  field('done', 'boolean'),
  field('dueOn', 'date'),
]

describe('isJsonNumber', () => {
  it('accepts JSON numbers and rejects other text', () => {
    for (const text of ['0', '-7', '12.50', '1e3', '-0.5E-2']) {
      expect(isJsonNumber(text)).toBe(true)
    }
    for (const text of ['', '01', '1.', '.5', '+1', '1,5', 'abc', ' 1', 'NaN', 'Infinity']) {
      expect(isJsonNumber(text)).toBe(false)
    }
  })
})

describe('buildRecordBody', () => {
  it('writes integers and decimals as JSON numbers with the typed text', () => {
    expect(buildRecordBody(fields, { priority: '7', amount: '12.50' })).toBe('{"values":{"priority":7,"amount":12.50}}')
  })

  it('keeps a decimal with more digits than a double holds', () => {
    expect(buildRecordBody(fields, { amount: '123456789012345678901.123456789' })).toBe(
      '{"values":{"amount":123456789012345678901.123456789}}',
    )
  })

  it('writes the version and only the given fields on update', () => {
    expect(buildRecordBody(fields, { title: 'x' }, 3)).toBe('{"version":3,"values":{"title":"x"}}')
  })

  it('writes number text that is not a JSON number as a string, so the server rejects it', () => {
    expect(buildRecordBody(fields, { amount: '1,5' })).toBe('{"values":{"amount":"1,5"}}')
    expect(buildRecordBody(fields, { priority: 'abc' })).toBe('{"values":{"priority":"abc"}}')
  })

  it('writes null, booleans and strings as JSON in field declaration order', () => {
    expect(buildRecordBody(fields, { dueOn: '2026-10-07', amount: null, done: true, title: 'a "b"' })).toBe(
      '{"values":{"title":"a \\"b\\"","amount":null,"done":true,"dueOn":"2026-10-07"}}',
    )
  })

  it('writes a number-like text field as a string', () => {
    expect(buildRecordBody(fields, { title: '7' })).toBe('{"values":{"title":"7"}}')
  })

  describe('a child collection', () => {
    const lines = {
      ...field('lines', 'child-collection'),
      fields: [field('description', 'text'), field('quantity', 'integer'), field('price', 'decimal')],
    }

    it('writes an empty row list', () => {
      expect(buildRecordBody([field('title', 'text'), lines], { lines: [] })).toBe('{"values":{"lines":[]}}')
    })

    it('writes each row with numbers as typed, in child field declaration order', () => {
      expect(buildRecordBody([lines], { lines: [{ price: '1.50', description: 'Pens', quantity: '2' }] })).toBe(
        '{"values":{"lines":[{"description":"Pens","quantity":2,"price":1.50}]}}',
      )
    })

    it('leaves the null fields of a row out', () => {
      expect(
        buildRecordBody([lines], {
          lines: [
            { description: null, quantity: '3', price: null },
            { description: 'Ink', quantity: null, price: null },
          ],
        }),
      ).toBe('{"values":{"lines":[{"quantity":3},{"description":"Ink"}]}}')
    })

    it('leaves a computed row field out even when it holds a value', () => {
      const computedLines = {
        ...lines,
        fields: [field('description', 'text'), { ...field('code', 'text'), computed: true }],
      }
      expect(buildRecordBody([computedLines], { lines: [{ description: 'Pens', code: 'PENS' }] })).toBe(
        '{"values":{"lines":[{"description":"Pens"}]}}',
      )
    })
  })

  it('leaves a computed field out even when it holds a value', () => {
    const withTotal = [...fields, { ...field('total', 'decimal'), computed: true }]
    expect(buildRecordBody(withTotal, { amount: '1.50', total: '3.00' }, 2)).toBe(
      '{"version":2,"values":{"amount":1.50}}',
    )
  })

  it('leaves a sequence field out even when it holds a value', () => {
    const withNumber = [{ ...field('number', 'text'), sequence: true }, ...fields]
    expect(buildRecordBody(withNumber, { number: 'PR-2026-00042', amount: '1.50' }, 2)).toBe(
      '{"version":2,"values":{"amount":1.50}}',
    )
  })
})
