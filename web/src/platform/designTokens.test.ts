import { describe, expect, it } from 'vitest'
import { colourTokens, designTokensFor } from './designTokens'

describe('colour tokens', () => {
  it.each(Object.entries(colourTokens))('%s has a light and a dark value', (_name, value) => {
    expect(value.light.trim()).not.toBe('')
    expect(value.dark.trim()).not.toBe('')
  })

  it.each(['light', 'dark'] as const)('resolves every colour for %s mode', (mode) => {
    const { colour } = designTokensFor(mode)
    expect(Object.keys(colour).sort()).toEqual(Object.keys(colourTokens).sort())
    for (const [name, value] of Object.entries(colourTokens)) {
      expect(colour[name as keyof typeof colour]).toBe(value[mode])
    }
  })
})
