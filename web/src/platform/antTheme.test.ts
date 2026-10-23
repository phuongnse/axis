import { theme } from 'antd'
import { describe, expect, it } from 'vitest'
import { antThemeFor } from './antTheme'
import { designTokensFor } from './designTokens'

// The theme config before the Axis token file. The mapping must give Ant Design the same design token.
const previousToken = {
  colorPrimary: '#2f54eb',
  borderRadius: 6,
  fontFamily: "'Inter', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
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
