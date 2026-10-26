import { describe, expect, it } from 'vitest'
import {
  colourTokens,
  designTokensFor,
  radiusTokens,
  shadowTokens,
  spacingTokens,
  typeTokens,
  type ColourName,
} from './designTokens'

// WCAG 2.x contrast of a colour token over a solid background.
function channels(value: string): [number, number, number, number] {
  const hex = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(value)
  if (hex) return [parseInt(hex[1], 16), parseInt(hex[2], 16), parseInt(hex[3], 16), 1]
  const rgba = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$/.exec(value)
  if (rgba) return [Number(rgba[1]), Number(rgba[2]), Number(rgba[3]), rgba[4] === undefined ? 1 : Number(rgba[4])]
  throw new Error(`Cannot parse the colour ${value}`)
}

function over(foreground: string, background: string): [number, number, number] {
  const [r, g, b, a] = channels(foreground)
  const [br, bg, bb] = channels(background)
  return [r * a + br * (1 - a), g * a + bg * (1 - a), b * a + bb * (1 - a)]
}

function luminance([r, g, b]: [number, number, number]): number {
  const linear = (c: number) => {
    const s = c / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b)
}

function contrast(foreground: string, background: string): number {
  const a = luminance(over(foreground, background))
  const b = luminance(channels(background).slice(0, 3) as [number, number, number])
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

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

describe.each(['light', 'dark'] as const)('colour contrast in %s mode', (mode) => {
  const names = Object.keys(colourTokens) as ColourName[]
  const textNames = ['text', 'textMuted', ...names.filter((name) => name.endsWith('Text'))] as ColourName[]
  const backgrounds = ['surface', 'canvas'] as const

  it('finds the text tokens in the token file', () => {
    expect(textNames.length).toBeGreaterThanOrEqual(7)
  })

  it.each(textNames.flatMap((name) => backgrounds.map((background) => [name, background] as const)))(
    '%s reaches 4.5:1 on %s',
    (name, background) => {
      expect(contrast(colourTokens[name][mode], colourTokens[background][mode])).toBeGreaterThanOrEqual(4.5)
    },
  )

  it.each(
    (['border', 'focusRing'] as const).flatMap((name) => backgrounds.map((background) => [name, background] as const)),
  )('%s reaches 3:1 on %s', (name, background) => {
    expect(contrast(colourTokens[name][mode], colourTokens[background][mode])).toBeGreaterThanOrEqual(3)
  })
})

describe('the favicon', () => {
  const files = import.meta.glob('../../public/favicon.svg', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>

  it('fills its square with the light primary colour', () => {
    const svg = Object.values(files)[0]
    expect(svg).toContain(`<rect width="32" height="32" rx="6" fill="${colourTokens.primary.light}"/>`)
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
      '--shadowOverlay': ' 0 2px 4px rgba(0,0,0,0.5) ',
    })
    expect(tokens.colour.primary).toBe('#13c2c2')
    expect(tokens.colour.text).toBe('rgba(1, 2, 3, 0.5)')
    expect(tokens.spacing.lg).toBe(28)
    expect(tokens.spacing.xs).toBe(4.5)
    expect(tokens.radius.base).toBe(2)
    expect(tokens.type.fontFamily).toBe('Georgia, serif')
    expect(tokens.type.codeFontFamily).toBe('Courier')
    expect(tokens.shadow.overlay).toBe('0 2px 4px rgba(0,0,0,0.5)')
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
      '--shadowOverlay': 'a{b}',
    })
    expect(tokens).toEqual(designTokensFor(mode))
    expect(tokens.colour.primary).toBe(colourTokens.primary[mode])
    expect(tokens.spacing.md).toBe(spacingTokens.md)
    expect(tokens.radius.base).toBe(radiusTokens.base)
    expect(tokens.type.fontFamily).toBe(typeTokens.fontFamily)
    expect(tokens.shadow.overlay).toBe(shadowTokens.overlay[mode])
  })

  it.each(['', '  ', 'none; color: red', '0 0 1px red }'])('keeps the token file shadow for "%s"', (value) => {
    expect(designTokensFor('light', { '--shadowOverlay': value }).shadow.overlay).toBe(shadowTokens.overlay.light)
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
