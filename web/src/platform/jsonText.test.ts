import { describe, expect, it } from 'vitest'
import { formatJsonText } from './jsonText'

describe('formatJsonText', () => {
  it('lays JSON out with two spaces and keeps every number as written', () => {
    expect(
      formatJsonText('{"amount":18500.10,"big":12345678901234567890.5,"lines":[{"q":1}],"e":{},"s":"a, b: {c}"}'),
    ).toBe(
      [
        '{',
        '  "amount": 18500.10,',
        '  "big": 12345678901234567890.5,',
        '  "lines": [',
        '    {',
        '      "q": 1',
        '    }',
        '  ],',
        '  "e": {},',
        '  "s": "a, b: {c}"',
        '}',
      ].join('\n'),
    )
  })

  it('keeps an escaped quote inside a string', () => {
    expect(formatJsonText('{"a":"say \\"hi\\""}')).toBe('{\n  "a": "say \\"hi\\""\n}')
  })

  it('gives back text that is not JSON as it is', () => {
    expect(formatJsonText('<html>Service Unavailable</html>')).toBe('<html>Service Unavailable</html>')
    expect(formatJsonText('{not json')).toBe('{not json')
  })
})
