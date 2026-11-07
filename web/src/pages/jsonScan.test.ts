import { describe, expect, it } from 'vitest'
import { scanJson } from './jsonScan'

describe('scanJson', () => {
  it.each([
    ['', 0],
    ['{', 1],
    ['{"a"', 4],
    ['{"a":', 5],
    ['{"a":1,}', 7],
    ['{"a" 1}', 5],
    ['{a:1}', 1],
    ['[1 2]', 3],
    ['[1,]', 3],
    ['"\\x"', 2],
    ['"\\u12G4"', 5],
    ['"a\nb"', 2],
    ['"abc', 4],
    ['01', 1],
    ['-', 1],
    ['1.', 2],
    ['1e', 2],
    ['+1', 0],
    ['tru', 0],
    ['truex', 4],
    ['{} {}', 3],
    ["{'a': 1}", 1],
  ])('finds that %j stops being JSON at %i', (text, offset) => {
    expect(scanJson(text).error).toBe(offset)
  })

  it.each([
    '{}',
    '[]',
    ' \t\r\n{ "a" : [ 1 , -2.5e+3 , true , false , null , "x\\n\\u00e9\\"\\/" ] } ',
    '0',
    '-0.1E-2',
    '"a"',
    '[[[[1]]]]',
    '{"a":{"b":{"c":[{}]}}}',
  ])('finds %j to be JSON', (text) => {
    expect(scanJson(text).error).toBeNull()
  })

  it('agrees with JSON.parse on what is valid', () => {
    const samples = [
      '{"a":1}',
      '{"a":1,}',
      '[01]',
      '[1.0]',
      '[.5]',
      '[1e5]',
      '["\\u00zz"]',
      '["\t"]',
      '[NaN]',
      '[-Infinity]',
      '{"a":1}}',
      '﻿{}',
      '[true,false,null]',
      '[nul]',
      '{"a":"b" "c":1}',
      '[1,2',
      '{"":0}',
      '"\\ud800"',
    ]
    for (const text of samples) {
      const valid = (() => {
        try {
          JSON.parse(text)
          return true
        } catch {
          return false
        }
      })()
      expect([text, scanJson(text).error === null]).toEqual([text, valid])
    }
  })

  it('finds the key and value of each nested pointer', () => {
    const text = '{"a": {"b": [10, "x"]}, "c": true}'
    const { spans } = scanJson(text)
    const at = (pointer: string) => {
      const span = spans.get(pointer)!
      return [span.key && text.slice(span.key.from, span.key.to), text.slice(span.value.from, span.value.to)]
    }

    expect(at('')).toEqual([undefined, text])
    expect(at('/a')).toEqual(['"a"', '{"b": [10, "x"]}'])
    expect(at('/a/b')).toEqual(['"b"', '[10, "x"]'])
    expect(at('/a/b/0')).toEqual([undefined, '10'])
    expect(at('/a/b/1')).toEqual([undefined, '"x"'])
    expect(at('/c')).toEqual(['"c"', 'true'])
  })

  it('escapes ~ and / in the pointer of a key', () => {
    const text = '{"a/b": 1, "m~n": 2, "~1": 3}'
    const { spans } = scanJson(text)
    const value = (pointer: string) => {
      const span = spans.get(pointer)!
      return text.slice(span.value.from, span.value.to)
    }

    expect(value('/a~1b')).toBe('1')
    expect(value('/m~0n')).toBe('2')
    expect(value('/~01')).toBe('3')
  })

  it('keeps the last of a repeated key and the places inside it', () => {
    const text = '{"a": {"x": 1}, "a": 2}'
    const { spans } = scanJson(text)

    expect(text.slice(spans.get('/a')!.value.from, spans.get('/a')!.value.to)).toBe('2')
    expect(spans.has('/a/x')).toBe(false)
  })

  it('holds the values before an error', () => {
    const { error, spans } = scanJson('{"a": 1, "b": }')

    expect(error).toBe(14)
    expect(spans.has('/a')).toBe(true)
    expect(spans.has('/b')).toBe(false)
  })
})
