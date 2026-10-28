import { describe, expect, it } from 'vitest'
import { setOwn } from './ownProperty'

describe('setOwn', () => {
  it('sets a property that reads, lists and serialises like an assigned one', () => {
    const target: Record<string, number> = {}

    setOwn(target, 'a', 1)
    setOwn(target, 'a', 2)

    expect(target.a).toBe(2)
    expect(Object.keys(target)).toEqual(['a'])
    expect(JSON.stringify(target)).toBe('{"a":2}')
  })

  it('keeps __proto__ as a key and leaves the prototype alone', () => {
    const target: Record<string, unknown> = {}

    setOwn(target, '__proto__', { polluted: 1 })

    expect(Object.getPrototypeOf(target)).toBe(Object.prototype)
    expect(({} as Record<string, unknown>).polluted).toBeUndefined()
    expect(Object.keys(target)).toEqual(['__proto__'])
    expect(JSON.stringify(target)).toBe('{"__proto__":{"polluted":1}}')
  })
})
