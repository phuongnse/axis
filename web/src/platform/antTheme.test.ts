import { theme } from 'antd'
import type { CSSProperties } from 'react'
import { describe, expect, it } from 'vitest'
import { antConfigFor, antThemeFor, fieldRules, tableTheme, textButtonClass } from './antTheme'
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
    // fillSubtle has no Ant Design token: the process graph and its icon tiles read it through useDesignTokens only.
    const { fillSubtle: _fillSubtle, ...colour } = designTokensFor(mode).colour
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
      onPrimary: token.colorTextLightSolid,
    }).toEqual(colour)
  })

  it('draws Ant Design secondary text in the muted text colour, as it does tertiary text', () => {
    const token = theme.getDesignToken(antThemeFor(mode))
    expect(token.colorTextSecondary).toBe(designTokensFor(mode).colour.textMuted)
    expect(token.colorTextSecondary).toBe(token.colorTextTertiary)
  })
})

describe.each(['light', 'dark'] as const)('the accessible colours of the Ant Design theme in %s mode', (mode) => {
  const config = antThemeFor(mode)
  const token = theme.getDesignToken(config)
  const { colour, shadow, spacing } = designTokensFor(mode)

  it('draws a placeholder in the muted text colour', () => {
    expect(token.colorTextPlaceholder).toBe(colour.textMuted)
  })

  it('draws a select arrow in the muted text colour and a checked radio in primaryText', () => {
    expect(config.components?.Select).toMatchObject({ colorTextQuaternary: colour.textMuted })
    expect(config.components?.Radio).toMatchObject({ colorPrimary: colour.primaryText })
  })

  it('gives a tab label no padding above it at any size and keeps the padding below', () => {
    const tabs = config.components?.Tabs
    expect(tabs).toMatchObject({
      horizontalItemPadding: `0 0 ${spacing.sm}px`,
      horizontalItemPaddingSM: `0 0 ${spacing.xs}px`,
      horizontalItemPaddingLG: `0 0 ${spacing.md}px`,
    })
    for (const name of ['horizontalItemPadding', 'horizontalItemPaddingSM', 'horizontalItemPaddingLG'] as const) {
      expect(String(tabs?.[name])).toMatch(/^0 /)
    }
  })

  it('draws links and state text in the text tokens', () => {
    expect(token.colorLink).toBe(colour.primaryText)
    expect(token.colorLinkHover).toBe(colour.primaryText)
    expect(token.colorLinkActive).toBe(colour.primaryText)
    expect(token.colorSuccessText).toBe(colour.successText)
    expect(token.colorWarningText).toBe(colour.warningText)
    expect(token.colorErrorText).toBe(colour.dangerText)
    expect(token.colorInfoText).toBe(colour.infoText)
  })

  it('draws validation messages, the selected tab, the selected menu entry, the current page and destructive menu entries in the text tokens', () => {
    expect(config.components?.Form).toMatchObject({ colorError: colour.dangerText })
    expect(config.components?.Dropdown).toMatchObject({ colorError: colour.dangerText })
    expect(config.components?.Tabs).toMatchObject({
      itemSelectedColor: colour.primaryText,
      itemHoverColor: colour.primaryText,
      itemActiveColor: colour.primaryText,
    })
    expect(config.components?.Menu).toMatchObject({
      itemSelectedColor: colour.primaryText,
      horizontalItemSelectedColor: colour.primaryText,
      collapsedWidth: 64,
      dangerItemColor: colour.dangerText,
      dangerItemHoverColor: colour.dangerText,
      dangerItemSelectedColor: colour.dangerText,
    })
    expect(config.components?.Pagination).toMatchObject({
      itemActiveColor: colour.primaryText,
      itemActiveColorHover: colour.primaryText,
      colorPrimary: colour.primaryText,
      colorPrimaryHover: colour.primaryText,
    })
  })

  it('draws a selected menu item in primaryText on the selected tint', () => {
    expect(config.components?.Dropdown).toMatchObject({ colorPrimary: colour.primaryText })
  })

  it('draws a dialog on surface in its content, header and footer', () => {
    expect(config.components?.Modal).toMatchObject({
      contentBg: colour.surface,
      headerBg: colour.surface,
      footerBg: colour.surface,
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

  it('draws a hovered or pressed default button in the text colour that passes contrast', () => {
    expect(config.components?.Button).toMatchObject({
      defaultHoverColor: colour.primaryText,
      defaultHoverBorderColor: colour.primaryText,
      defaultActiveColor: colour.primaryText,
      defaultActiveBorderColor: colour.primaryText,
    })
  })
})

describe.each(['light', 'dark'] as const)('the shared dialog frame in %s mode', (mode) => {
  const { spacing } = designTokensFor(mode)
  // The styles of the frame's parts, as antConfigFor gives them to every dialog.
  const styles = antConfigFor(mode).modal?.styles as { body?: CSSProperties; footer?: CSSProperties } | undefined

  it('keeps spacing-lg between the body and the buttons', () => {
    // The body's bottom padding and the footer's top margin add up to the gap above the buttons.
    expect(Number(styles?.footer?.marginTop) + spacing.xs).toBe(spacing.lg)
  })

  it('keeps spacing-xs under the body, so a focus ring has room', () => {
    expect(styles?.body?.padding).toBe(`${spacing.xs}px ${spacing.lg}px`)
  })
})

describe.each(['light', 'dark'] as const)('the shared field rules in %s mode', (mode) => {
  const { colour } = designTokensFor(mode)
  const { theme: themeConfig, checkbox, radio, form } = antConfigFor(mode)

  it('draws the border of an input, number input, select, date picker, mentions, cascader and tree select in dangerText on an error', () => {
    for (const name of [
      'Input',
      'InputNumber',
      'Select',
      'DatePicker',
      'Mentions',
      'Cascader',
      'TreeSelect',
    ] as const) {
      expect(themeConfig?.components?.[name]).toMatchObject({
        colorError: colour.dangerText,
        colorErrorBorderHover: colour.dangerText,
      })
    }
  })

  it('draws the border of the same inputs in warningText on a warning, and the form message in warningText', () => {
    expect(themeConfig?.components?.Form).toMatchObject({ colorWarning: colour.warningText })
    for (const name of [
      'Input',
      'InputNumber',
      'Select',
      'DatePicker',
      'Mentions',
      'Cascader',
      'TreeSelect',
    ] as const) {
      expect(themeConfig?.components?.[name]).toMatchObject({
        colorWarning: colour.warningText,
        colorWarningBorderHover: colour.warningText,
      })
    }
  })

  it('keeps no space below a form item, as the form layout gives the gap', () => {
    expect(themeConfig?.components?.Form).toMatchObject({ itemMarginBottom: 0 })
  })

  it('sits a checkbox and a radio box at the top, level with the first line of a label that wraps', () => {
    const box = { styles: { icon: { alignSelf: 'flex-start', marginTop: expect.closeTo(3) } } }
    expect(checkbox).toMatchObject(box)
    expect(radio).toMatchObject(box)
  })

  it("gives a form's messages a 4px gap and a 14/20 line through the form's help part", () => {
    expect(form?.styles).toEqual({ help: { marginTop: 4, fontSize: 14, lineHeight: '20px' } })
  })

  it('names no Ant Design form message class, so an item grows by its message', () => {
    expect(fieldRules(mode)).not.toMatch(/\.ant-form-item-(explain|margin-offset)/)
  })

  it('gives a text button of AxisButton the light fill of its hover on keyboard focus', () => {
    const hoverFill = theme.getDesignToken(antThemeFor(mode)).colorBgTextHover
    expect(fieldRules(mode)).toContain(`.${textButtonClass}:not(:disabled):focus-visible { background: ${hoverFill}; }`)
  })

  it('names no Ant Design button, checkbox or radio class', () => {
    expect(fieldRules(mode)).not.toMatch(/\.ant-(btn|checkbox|radio)/)
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

  it('takes the width of every dialog from a dialog size token', () => {
    const dialogs = Object.values(sources).reduce((total, text) => total + (text.match(/<Modal\b/g)?.length ?? 0), 0)
    expect(dialogs).toBeGreaterThan(0)
    const unsized = Object.entries(sources)
      .filter(([, text]) => {
        const sized = text.match(/\bwidth=\{[^}\n]*\bdialog\.(small|medium|large)\b/g)?.length ?? 0
        return (text.match(/<Modal\b/g)?.length ?? 0) !== sized
      })
      .map(([file]) => file)
    expect(unsized).toEqual([])
  })

  /** The files that draw an Ant Design dialog's OK or Cancel button, which DialogFooter replaces. */
  function antDialogButtons(files: Record<string, string>) {
    return Object.entries(files)
      .filter(([, text]) => /\bokText=|\bonOk=|\bokButtonProps\b|\bOkBtn\b|\bCancelBtn\b/.test(text))
      .map(([file]) => file)
  }

  it("draws every dialog's Cancel and finish button through DialogFooter, see docs/reference/actions-and-controls.md#buttons", () => {
    expect(antDialogButtons(sources)).toEqual([])
  })

  it("names a file that uses Ant Design's dialog buttons", () => {
    expect(antDialogButtons({ '../pages/Sample.tsx': '<Modal\n  okButtonProps={{\n    danger: true }}' })).toEqual([
      '../pages/Sample.tsx',
    ])
  })

  it("puts every form's label above its input", () => {
    const beside = Object.entries(sources)
      .filter(([, text]) => {
        const forms = text.match(/<Form[\s>]/g)?.length ?? 0
        return forms !== (text.match(/\blayout="vertical"/g)?.length ?? 0)
      })
      .map(([file]) => file)
    expect(beside).toEqual([])
  })

  /** The files outside the sign-in pages with a form that does not name the shared required mark. */
  function unmarked(files: Record<string, string>) {
    return Object.entries(files)
      .filter(([file]) => !file.includes('/identitySignIn/'))
      .filter(
        ([, text]) =>
          (text.match(/<Form[\s>]/g)?.length ?? 0) !== (text.match(/\brequiredMark=\{requiredMark\}/g)?.length ?? 0),
      )
      .map(([file]) => file)
  }

  it('marks the required fields of every form with the shared required mark, outside the sign-in pages', () => {
    expect(unmarked(sources)).toEqual([])
  })

  it('names a form without the shared required mark', () => {
    expect(unmarked({ '../pages/Sample.tsx': '<Form layout="vertical">' })).toEqual(['../pages/Sample.tsx'])
  })

  /** The files with a form that does not take the focus to its first field in error, the sign-in pages included. */
  function unfocused(files: Record<string, string>) {
    return Object.entries(files)
      .filter(
        ([, text]) =>
          (text.match(/<Form[\s>]/g)?.length ?? 0) !==
          (text.match(/\bscrollToFirstError=\{\{ focus: true \}\}/g)?.length ?? 0),
      )
      .map(([file]) => file)
  }

  it('takes the focus to the first field in error in every form', () => {
    expect(unfocused(sources)).toEqual([])
  })

  it('names a form that does not take the focus to its first error', () => {
    expect(unfocused({ '../pages/Sample.tsx': '<Form layout="vertical">' })).toEqual(['../pages/Sample.tsx'])
  })

  /** The files that render Ant Design's `<Form>` outside AxisForm, the form widget and the filter bar. */
  function antForms(files: Record<string, string>) {
    return Object.entries(files)
      .filter(([file]) => !/^\.\/(AxisForm|FormWidget|FilterBar)\.tsx$/.test(file))
      .filter(([, text]) => /<Form[\s>]/.test(text))
      .map(([file]) => file)
  }

  it('draws every form through AxisForm, see docs/reference/actions-and-controls.md#fields', () => {
    expect(antForms(sources)).toEqual([])
  })

  it("names a file that renders Ant Design's form, and not one that only uses its hooks and items", () => {
    expect(antForms({ '../pages/Sample.tsx': '<Form layout="vertical">' })).toEqual(['../pages/Sample.tsx'])
    expect(antForms({ '../pages/Sample.tsx': 'const [form] = Form.useForm()\n<Form.Item name="a" />' })).toEqual([])
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

describe('the table theme', () => {
  it('pads a cell by spacing-xs above and below and spacing-lg on each side', () => {
    const { spacing } = designTokensFor('light')
    expect(tableTheme(designTokensFor('light')).components?.Table).toEqual({
      cellPaddingBlock: spacing.xs,
      cellPaddingInline: spacing.lg,
    })
  })

  it('pads a compact cell by spacing-sm on each side', () => {
    const { spacing } = designTokensFor('light')
    expect(tableTheme(designTokensFor('light'), true).components?.Table).toEqual({
      cellPaddingBlock: spacing.xs,
      cellPaddingInline: spacing.sm,
    })
  })
})
