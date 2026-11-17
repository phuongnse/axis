import { describe, expect, it } from 'vitest'
import { jsonRows, parseJsonText, textRows } from './valueRows'

describe('valueRows', () => {
  it('makes a row of each value at any depth, named by its path, and keeps a number as written', () => {
    const rows = jsonRows(
      parseJsonText('{"amount":18500.10,"note":"","gone":null,"ok":true,"lines":[{"q":1}],"tags":[],"meta":{}}'),
    )
    expect(rows.map((row) => [row.name, row.value])).toEqual([
      ['amount', { kind: 'number', text: '18500.10' }],
      ['note', { kind: 'empty' }],
      ['gone', { kind: 'null' }],
      ['ok', { kind: 'boolean', text: 'true' }],
      ['lines[0].q', { kind: 'number', text: '1' }],
      ['tags', { kind: 'items', count: 0 }],
      ['meta', { kind: 'fields', count: 0 }],
    ])
  })

  it('gives undefined for text that is not JSON', () => {
    expect(parseJsonText('<html></html>')).toBeUndefined()
  })

  it('lists headers as text', () => {
    expect(textRows({ Accept: 'application/json' })).toEqual([
      { key: 'Accept', name: 'Accept', value: { kind: 'text', text: 'application/json' } },
    ])
  })
})
