import { theme } from 'antd'
import { describe, expect, it } from 'vitest'
import { antThemeFor } from './antTheme'
import {
  colourTokens,
  designTokensFor,
  pageTokenProperties,
  radiusTokens,
  shadowTokens,
  spacingTokens,
  typeTokens,
} from './designTokens'

describe.each(['light', 'dark'] as const)('the Ant Design theme in %s mode', (mode) => {
  it('resolves the colours the components read through useDesignTokens', () => {
    const token = theme.getDesignToken(antThemeFor(mode))
    const { colour } = designTokensFor(mode)
    expect({
      primary: token.colorPrimary,
      primaryText: token.colorPrimaryText,
      success: token.colorSuccess,
      successText: token.colorSuccessText,
      warning: token.colorWarning,
      warningText: token.colorWarningText,
      danger: token.colorError,
      dangerText: token.colorErrorText,
      info: token.colorInfo,
      infoText: token.colorInfoText,
      canvas: token.colorBgLayout,
      surface: token.colorBgContainer,
      text: token.colorText,
      textMuted: token.colorTextTertiary,
      border: token.colorBorder,
      borderSubtle: token.colorBorderSecondary,
      divider: token.colorSplit,
      focusRing: token.controlOutline,
    }).toEqual(colour)
  })
})

describe.each(['light', 'dark'] as const)('the accessible colours of the Ant Design theme in %s mode', (mode) => {
  const config = antThemeFor(mode)
  const token = theme.getDesignToken(config)
  const { colour, shadow } = designTokensFor(mode)

  it('draws links and state text in the text tokens', () => {
    expect(token.colorLink).toBe(colour.primaryText)
    expect(token.colorLinkHover).toBe(colour.primaryText)
    expect(token.colorLinkActive).toBe(colour.primaryText)
    expect(token.colorSuccessText).toBe(colour.successText)
    expect(token.colorWarningText).toBe(colour.warningText)
    expect(token.colorErrorText).toBe(colour.dangerText)
    expect(token.colorInfoText).toBe(colour.infoText)
  })

  it('draws validation messages, the selected tab and the selected menu entry in the text tokens', () => {
    expect(config.components?.Form).toMatchObject({ colorError: colour.dangerText })
    expect(config.components?.Tabs).toMatchObject({
      itemSelectedColor: colour.primaryText,
      itemHoverColor: colour.primaryText,
      itemActiveColor: colour.primaryText,
    })
    expect(config.components?.Menu).toMatchObject({
      itemSelectedColor: colour.primaryText,
      horizontalItemSelectedColor: colour.primaryText,
    })
  })

  it('draws keyboard focus as a solid 2px focusRing', () => {
    expect(token.colorPrimaryBorder).toBe(colour.focusRing)
    expect(token.controlOutline).toBe(colour.focusRing)
    expect(token.lineWidthFocus).toBe(2)
    expect(token.controlOutlineWidth).toBe(2)
  })

  it('casts the overlay shadow on overlays and no other shadow', () => {
    expect(token.boxShadow).toBe(shadow.overlay)
    expect(token.boxShadowSecondary).toBe(shadow.overlay)
    expect(token.boxShadowTertiary).toBe('none')
    expect(config.components?.Button).toMatchObject({
      defaultShadow: 'none',
      primaryShadow: 'none',
      dangerShadow: 'none',
    })
  })
})

describe('the Ant Design theme and reduced motion', () => {
  it('turns motion off when the browser asks for reduced motion', () => {
    expect(theme.getDesignToken(antThemeFor('light', undefined, true)).motion).toBe(false)
  })

  it('leaves the motion token unset otherwise, so an outer provider can still turn it off', () => {
    expect(antThemeFor('light').token?.motion).toBeUndefined()
    expect(theme.getDesignToken(antThemeFor('light')).motion).toBe(true)
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
      [pageTokenProperties.shadow.overlay]: shadowTokens.overlay[mode],
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
