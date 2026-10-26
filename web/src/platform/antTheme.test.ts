import { theme } from 'antd'
import { describe, expect, it } from 'vitest'
import { antThemeFor } from './antTheme'
import {
  colourTokens,
  designTokensFor,
  pageTokenProperties,
  radiusTokens,
  spacingTokens,
  typeTokens,
} from './designTokens'

// The theme config before the Axis token file. The mapping must give Ant Design the same design token. The
// fonts are the intended change: IBM Plex Sans replaced Inter and IBM Plex Mono leads the code fonts.
const previousToken = {
  colorPrimary: '#2f54eb',
  borderRadius: 6,
  fontFamily: "'IBM Plex Sans', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
  fontFamilyCode: "'IBM Plex Mono', 'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, Courier, monospace",
}

describe.each(['light', 'dark'] as const)('the Ant Design theme in %s mode', (mode) => {
  const algorithm = mode === 'dark' ? theme.darkAlgorithm : theme.defaultAlgorithm

  it('gives the same design token as the previous theme config', () => {
    expect(theme.getDesignToken(antThemeFor(mode))).toEqual(theme.getDesignToken({ algorithm, token: previousToken }))
  })

  it('resolves the colours the components read through useDesignTokens', () => {
    const token = theme.getDesignToken(antThemeFor(mode))
    const { colour } = designTokensFor(mode)
    expect({
      primary: token.colorPrimary,
      success: token.colorSuccess,
      warning: token.colorWarning,
      danger: token.colorError,
      info: token.colorInfo,
      canvas: token.colorBgLayout,
      surface: token.colorBgContainer,
      text: token.colorText,
      textMuted: token.colorTextTertiary,
      border: token.colorBorder,
      borderSubtle: token.colorBorderSecondary,
      divider: token.colorSplit,
    }).toEqual(colour)
  })
})

describe('the Ant Design theme with page values', () => {
  const token = (config: ReturnType<typeof antThemeFor>) => theme.getDesignToken(config)

  it('uses the colour, spacing, radius and font values the page declares', () => {
    const result = token(
      antThemeFor('light', {
        '--primary': '#13c2c2',
        '--canvas': '#fafafa',
        '--spacing-md': '20px',
        '--radius-base': '10',
        '--font-sans': 'Georgia, serif',
      }),
    )
    expect(result.colorPrimary).toBe('#13c2c2')
    expect(result.colorBgLayout).toBe('#fafafa')
    expect(result.padding).toBe(20)
    expect(result.borderRadius).toBe(10)
    expect(result.fontFamily).toBe('Georgia, serif')
  })

  it('keeps the token file value for an undeclared or unusable value', () => {
    expect(
      token(antThemeFor('light', { '--primary': 'banana', '--radius-base': 'large', '--spacing-md': '' })),
    ).toEqual(token(antThemeFor('light')))
  })

  it('uses an edited primary colour as the seed in dark mode', () => {
    expect(antThemeFor('dark', { '--primary': '#13c2c2' }).token?.colorPrimary).toBe('#13c2c2')
  })

  describe.each(['light', 'dark'] as const)('in %s mode', (mode) => {
    const fileValues = {
      ...Object.fromEntries(
        Object.entries(colourTokens).map(([name, value]) => [
          pageTokenProperties.colour[name as keyof typeof colourTokens],
          value[mode],
        ]),
      ),
      ...Object.fromEntries(
        Object.entries(spacingTokens).map(([name, value]) => [
          pageTokenProperties.spacing[name as keyof typeof spacingTokens],
          `${value}px`,
        ]),
      ),
      [pageTokenProperties.radius.base]: String(radiusTokens.base),
      [pageTokenProperties.type.fontFamily]: typeTokens.fontFamily,
      [pageTokenProperties.type.codeFontFamily]: typeTokens.codeFontFamily,
    }

    it("gives today's design token without values, with none and with the token file's own values", () => {
      const today = token(antThemeFor(mode))
      expect(token(antThemeFor(mode, {}))).toEqual(today)
      expect(token(antThemeFor(mode, fileValues))).toEqual(today)
    })
  })
})

describe('theme values', () => {
  const sources = import.meta.glob(
    ['../**/*.{ts,tsx}', '!../**/*.test.{ts,tsx}', '!./designTokens.ts', '!./antTheme.ts'],
    {
      query: '?raw',
      import: 'default',
      eager: true,
    },
  ) as Record<string, string>

  const forbidden: Record<string, RegExp> = {
    'a hard-coded colour': /(['"`])[^'"`\n]*#[0-9a-fA-F]{3,8}\b[^'"`\n]*\1/,
    'a colour function': /\brgba?\(|\bhsla?\(/,
    'a font family': /fontFamily:\s*['"`](?!inherit)/,
    'a radius': /borderRadius:\s*\d/,
    'an inline theme': /theme=\{\{/,
    'theme.useToken': /theme\.useToken/,
  }

  it('finds the sources', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50)
  })

  it('keeps theme values in the token file and the mapping only', () => {
    const found = Object.entries(sources).flatMap(([file, text]) =>
      Object.entries(forbidden)
        .filter(([, pattern]) => pattern.test(text))
        .map(([what]) => `${file}: ${what}`),
    )
    expect(found).toEqual([])
  })
})
