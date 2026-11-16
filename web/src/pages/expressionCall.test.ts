import { describe, expect, it } from 'vitest'
import type { ExpressionScopeFunction } from '../platform/studio'
import { callAt } from './expressionCall'

const functions = ['contains', 'if', 'round'].map((name): ExpressionScopeFunction => ({
  name,
  parameters: [],
  result: 'text',
  group: 'text',
  example: '',
  description: '',
}))

function at(before: string) {
  const call = callAt(before, functions)
  return call && { name: call.fn.name, argument: call.argument }
}

describe('callAt', () => {
  it('counts the argument by the commas before the cursor', () => {
    expect(at("contains(title, 'x")).toEqual({ name: 'contains', argument: 1 })
    expect(at('contains(')).toEqual({ name: 'contains', argument: 0 })
  })

  it('does not count a comma inside quotes', () => {
    expect(at("contains(title, 'a,b")).toEqual({ name: 'contains', argument: 1 })
    expect(at("contains('it''s, x")).toEqual({ name: 'contains', argument: 0 })
  })

  it('does not count a comma inside nested brackets', () => {
    expect(at("if(status in ('a', 'b")).toEqual({ name: 'if', argument: 0 })
    expect(at('if(a, round(x, ')).toEqual({ name: 'round', argument: 1 })
    expect(at('if(round(x, 2), ')).toEqual({ name: 'if', argument: 1 })
  })

  it('reports the call around a plain bracket', () => {
    expect(at('if((a, b')).toEqual({ name: 'if', argument: 0 })
  })

  it('reports no call outside one', () => {
    expect(at("contains(title, 'x')")).toBeNull()
    expect(at('')).toBeNull()
    expect(at('title, ')).toBeNull()
  })

  it('matches the name ignoring letter case', () => {
    expect(at('Contains(')).toEqual({ name: 'contains', argument: 0 })
  })

  it('reports no call to a name the scope does not list', () => {
    expect(at('rule(')).toBeNull()
    expect(at('foo:bar(')).toBeNull()
    expect(at('foo:contains(')).toBeNull()
  })
})
