import { describe, expect, it } from 'vitest'
import { parseRecordJson } from './records'

describe('parseRecordJson', () => {
  it('keeps the source text of numbers and leaves strings unchanged', () => {
    expect(parseRecordJson('{"a":1250.50,"b":12345678901234567890,"c":"x","d":[1e3,true,null]}')).toEqual({
      a: '1250.50',
      b: '12345678901234567890',
      c: 'x',
      d: ['1e3', true, null],
    })
  })
})
