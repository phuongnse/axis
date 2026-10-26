import { describe, expect, it } from 'vitest'
import { colourTokens, designTokensFor, radiusTokens, spacingTokens, typeTokens } from './designTokens'

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

describe('tokens with page values', () => {
  it('uses the values the theme can use', () => {
    const tokens = designTokensFor('light', {
      '--primary': ' #13c2c2 ',
      '--text': 'rgba(1, 2, 3, 0.5)',
      '--border': 'hsl(10 20% 30%)',
      '--spacing-lg': '28px',
      '--spacing-xs': '4.5',
      '--radius-base': '2px',
      '--font-sans': 'Georgia, serif',
      '--font-code': 'Courier',
    })
    expect(tokens.colour.primary).toBe('#13c2c2')
    expect(tokens.colour.text).toBe('rgba(1, 2, 3, 0.5)')
    expect(tokens.spacing.lg).toBe(28)
    expect(tokens.spacing.xs).toBe(4.5)
    expect(tokens.radius.base).toBe(2)
    expect(tokens.type.fontFamily).toBe('Georgia, serif')
    expect(tokens.type.codeFontFamily).toBe('Courier')
  })

  it.each(['light', 'dark'] as const)('keeps the token file value for anything else in %s mode', (mode) => {
    const tokens = designTokensFor(mode, {
      '--primary': 'banana',
      '--success': '#12',
      '--warning': 'var(--x)',
      '--spacing-md': '1em',
      '--spacing-sm': '',
      '--radius-base': 'large',
      '--font-sans': '  ',
    })
    expect(tokens).toEqual(designTokensFor(mode))
    expect(tokens.colour.primary).toBe(colourTokens.primary[mode])
    expect(tokens.spacing.md).toBe(spacingTokens.md)
    expect(tokens.radius.base).toBe(radiusTokens.base)
    expect(tokens.type.fontFamily).toBe(typeTokens.fontFamily)
  })

  it('does not change the token file', () => {
    designTokensFor('light', { '--spacing-md': '99', '--radius-base': '99', '--font-sans': 'Georgia' })
    expect(spacingTokens.md).toBe(16)
    expect(radiusTokens.base).toBe(6)
    expect(typeTokens.fontFamily).toContain('IBM Plex Sans')
  })

  it('names IBM Plex first and no longer names Inter', () => {
    expect(typeTokens.fontFamily.startsWith("'IBM Plex Sans'")).toBe(true)
    expect(typeTokens.codeFontFamily.startsWith("'IBM Plex Mono'")).toBe(true)
    expect(typeTokens.fontFamily).not.toContain('Inter')
    expect(typeTokens.codeFontFamily).not.toContain('Inter')
  })
})
