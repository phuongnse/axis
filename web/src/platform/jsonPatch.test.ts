import { describe, expect, it } from 'vitest'
import { diffPatch, getAt, setAt } from './jsonPatch'

describe('diffPatch', () => {
  it('gives no operation for equal values', () => {
    expect(diffPatch({ a: [1, { b: 2 }] }, { a: [1, { b: 2 }] })).toEqual([])
  })

  it('replaces one nested value', () => {
    const before = { fields: [{ name: 'a' }, { name: 'b', maxLength: 200 }] }
    const after = { fields: [{ name: 'a' }, { name: 'b', maxLength: 250 }] }
    expect(diffPatch(before, after)).toEqual([{ op: 'replace', path: '/fields/1/maxLength', value: 250 }])
  })

  it('adds a new key and removes a missing one', () => {
    expect(diffPatch({ a: 1, b: 2 }, { a: 1, c: 3 })).toEqual([
      { op: 'remove', path: '/b' },
      { op: 'add', path: '/c', value: 3 },
    ])
  })

  it('replaces a whole array when its length changed', () => {
    expect(diffPatch({ values: ['a', 'b'] }, { values: ['a', 'b', 'c'] })).toEqual([
      { op: 'replace', path: '/values', value: ['a', 'b', 'c'] },
    ])
  })

  it('escapes the characters of a pointer segment', () => {
    expect(diffPatch({ 'a/b': { 'c~d': 1 } }, { 'a/b': { 'c~d': 2 } })).toEqual([
      { op: 'replace', path: '/a~1b/c~0d', value: 2 },
    ])
  })
})

describe('getAt and setAt', () => {
  it('reads a nested value and answers undefined for a missing one', () => {
    const value = { a: [{ 'b/c': 1 }] }
    expect(getAt(value, '/a/0/b~1c')).toBe(1)
    expect(getAt(value, '/a/1/b')).toBeUndefined()
    expect(getAt(value, '')).toBe(value)
  })

  it('copies the path it changes and keeps the original', () => {
    const value = { a: { b: 1 }, c: { d: 2 } }
    const next = setAt(value, '/a/b', 5) as typeof value
    expect(next).toEqual({ a: { b: 5 }, c: { d: 2 } })
    expect(next.c).toBe(value.c)
    expect(value.a.b).toBe(1)
  })

  it('deletes an object key and splices an array index when the value is undefined', () => {
    expect(setAt({ a: 1, b: 2 }, '/a', undefined)).toEqual({ b: 2 })
    expect(setAt({ list: ['x', 'y', 'z'] }, '/list/1', undefined)).toEqual({ list: ['x', 'z'] })
  })

  it('creates the missing objects on the way', () => {
    expect(setAt({}, '/label/textKey', 'k')).toEqual({ label: { textKey: 'k' } })
  })
})
