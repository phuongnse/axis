import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { contrast } from '../test/contrast'
import {
  colourTokens,
  designTokensFor,
  radiusTokens,
  shadowTokens,
  sizeTokens,
  spacingTokens,
  typeTokens,
  type ColourName,
} from './designTokens'

// Vitest runs from the web folder, as `npm test --prefix web` does.
function frontendDocBullet(label: string): string {
  const doc = readFileSync(join(process.cwd(), '../docs/reference/frontend.md'), 'utf8').replace(/\s+/g, ' ')
  return new RegExp(`- \\*\\*${label}\\.\\*\\*(.+?)(?= - \\*\\*|$)`).exec(doc)?.[1] ?? ''
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

  it('are listed in the frontend doc with the same names', () => {
    const names = new Set([...frontendDocBullet('Colours').matchAll(/`(\w+)`/g)].map((match) => match[1]))
    expect([...names].sort()).toEqual(Object.keys(colourTokens).sort())
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

  it('onPrimary reaches 4.5:1 on primary', () => {
    expect(contrast(colourTokens.onPrimary[mode], colourTokens.primary[mode])).toBeGreaterThanOrEqual(4.5)
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

  it('names IBM Plex first', () => {
    expect(typeTokens.fontFamily.startsWith("'IBM Plex Sans'")).toBe(true)
    expect(typeTokens.codeFontFamily.startsWith("'IBM Plex Mono'")).toBe(true)
  })
})

describe('size tokens', () => {
  it('are the shell bar, the small control, the table row and the dialog widths', () => {
    expect(sizeTokens).toEqual({
      bar: 48,
      smallControl: 24,
      tableRow: 40,
      dialog: { small: 400, medium: 560, large: 880 },
    })
  })

  it.each(['light', 'dark'] as const)('are returned for %s mode', (mode) => {
    expect(designTokensFor(mode).size).toEqual(sizeTokens)
  })

  it('are listed in the frontend doc with the same names and pixel values', () => {
    const names = [...frontendDocBullet('Sizes').matchAll(/`(\w+)`/g)].map((match) => match[1])
    const dialogSizes = Object.fromEntries(
      [...frontendDocBullet('Dialog sizes').matchAll(/`(\w+)` \((\d+) px\)/g)].map((match) => [
        match[1],
        Number(match[2]),
      ]),
    )

    const numeric = Object.keys(sizeTokens).filter(
      (name) => typeof sizeTokens[name as keyof typeof sizeTokens] === 'number',
    )
    expect(names.sort()).toEqual(numeric.sort())
    expect(dialogSizes).toEqual(sizeTokens.dialog)
  })

  it('are not changed by a page', () => {
    expect(designTokensFor('light', { '--size-bar': '99', '--size-tableRow': '99' }).size).toEqual(sizeTokens)
  })
})
