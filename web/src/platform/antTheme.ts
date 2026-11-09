import { theme, type ConfigProviderProps, type ThemeConfig } from 'antd'
import { colourTokens, designTokensFor, type ColourName, type DesignTokens, type PageTokenValues } from './designTokens'
import type { ThemeMode } from './themeMode'

// The only file that names Ant Design tokens. It maps the Axis tokens to Ant Design's theme.

// colorLink is a seed too, so the dark algorithm would shade it. A link must be exactly the text token.
const keepLink: typeof theme.darkAlgorithm = (seed, map) => {
  const mapped = map ?? theme.defaultAlgorithm(seed)
  return { ...mapped, colorLink: seed.colorLink }
}

/**
 * The theme for one mode. Ant Design keeps its algorithm and derives hover and active colours from it. Page values
 * replace the token file's values where the theme can use them. Under reduced motion, Ant Design shows and hides
 * its components at once, see D97.
 */
export function antThemeFor(mode: ThemeMode, page?: PageTokenValues, reducedMotion = false): ThemeConfig {
  const { colour, type, spacing, radius, shadow } = designTokensFor(mode, page)
  // Ant Design derives the shade for the mode from a seed. A colour that still equals the token file's value for
  // the mode keeps the light seed, as the dark value is already a shade. Any other colour is the seed itself.
  const seed = (name: ColourName) =>
    colour[name].toLowerCase() === colourTokens[name][mode] ? colourTokens[name].light : colour[name]
  // A field with an error draws its border in the text colour, which meets 3:1 contrast in both modes.
  const errorBorder = { colorError: colour.dangerText, colorErrorBorderHover: colour.dangerText }
  // Proposed: a field with a warning draws its border in the warning text colour, as its message is, for the same reason.
  const warningBorder = { colorWarning: colour.warningText, colorWarningBorderHover: colour.warningText }
  const statusBorder = { ...errorBorder, ...warningBorder }
  return {
    algorithm: [mode === 'dark' ? theme.darkAlgorithm : theme.defaultAlgorithm, keepLink],
    token: {
      // Seed colours: Ant Design derives their shade for the mode from the seed.
      colorPrimary: seed('primary'),
      colorSuccess: seed('success'),
      colorWarning: seed('warning'),
      colorError: seed('danger'),
      colorInfo: seed('info'),
      colorBgLayout: colour.canvas,
      colorBgContainer: colour.surface,
      colorText: colour.text,
      colorTextTertiary: colour.textMuted,
      // A placeholder is text that a person reads, so it meets 4.5:1 contrast as well.
      colorTextPlaceholder: colour.textMuted,
      // Text colours: Ant Design draws text in these as they are, not as seeds.
      colorPrimaryText: colour.primaryText,
      colorSuccessText: colour.successText,
      colorWarningText: colour.warningText,
      colorErrorText: colour.dangerText,
      colorInfoText: colour.infoText,
      colorLink: colour.primaryText,
      colorLinkHover: colour.primaryText,
      colorLinkActive: colour.primaryText,
      // Keyboard focus: a solid 2px ring. Ant Design draws input focus as a control outline and other focus as an outline.
      colorPrimaryBorder: colour.focusRing,
      lineWidthFocus: 2,
      controlOutline: colour.focusRing,
      controlOutlineWidth: 2,
      // The overlay shadow is the only shadow.
      boxShadow: shadow.overlay,
      boxShadowSecondary: shadow.overlay,
      boxShadowTertiary: 'none',
      colorBorder: colour.border,
      colorBorderSecondary: colour.borderSubtle,
      colorSplit: colour.divider,
      fontFamily: type.fontFamily,
      fontFamilyCode: type.codeFontFamily,
      fontSize: type.fontSize,
      paddingXS: spacing.xs,
      paddingSM: spacing.sm,
      padding: spacing.md,
      paddingLG: spacing.lg,
      paddingXL: spacing.xl,
      marginXS: spacing.xs,
      marginSM: spacing.sm,
      margin: spacing.md,
      marginLG: spacing.lg,
      marginXL: spacing.xl,
      borderRadius: radius.base,
      // Left unset otherwise, so an outer provider that turns motion off still applies.
      ...(reducedMotion && { motion: false }),
    },
    components: {
      // Proposed: a form's warning message, such as a check that could not run, takes the text colour that passes contrast.
      Form: { colorError: colour.dangerText, colorWarning: colour.warningText },
      Input: statusBorder,
      InputNumber: statusBorder,
      Select: statusBorder,
      DatePicker: statusBorder,
      Mentions: statusBorder,
      Cascader: statusBorder,
      TreeSelect: statusBorder,
      // A selected item draws in primaryText on the selected tint, see docs/reference/actions-and-controls.md#menus.
      // Every dropdown menu keeps spacing-xs round its items, see docs/reference/shell.md#app-switcher-and-preferences.
      Dropdown: { colorError: colour.dangerText, colorPrimary: colour.primaryText, paddingXXS: spacing.xs },
      Modal: { contentBg: colour.surface, headerBg: colour.surface, footerBg: colour.surface },
      // A tab's label starts at the top of its bar, so the padding above it is removed and the one below is kept.
      Tabs: {
        itemSelectedColor: colour.primaryText,
        itemHoverColor: colour.primaryText,
        itemActiveColor: colour.primaryText,
        horizontalItemPadding: `0 0 ${spacing.sm}px`,
        horizontalItemPaddingSM: `0 0 ${spacing.xs}px`,
        horizontalItemPaddingLG: `0 0 ${spacing.md}px`,
      },
      Menu: {
        itemSelectedColor: colour.primaryText,
        horizontalItemSelectedColor: colour.primaryText,
        collapsedWidth: 64,
        dangerItemColor: colour.dangerText,
        dangerItemHoverColor: colour.dangerText,
        dangerItemSelectedColor: colour.dangerText,
      },
      Pagination: {
        itemActiveColor: colour.primaryText,
        itemActiveColorHover: colour.primaryText,
        colorPrimary: colour.primaryText,
        colorPrimaryHover: colour.primaryText,
      },
      // Proposed: an alert's icon is drawn in the token's colour, so it takes the text colour that passes 3:1 in both modes.
      Alert: {
        colorSuccess: colour.successText,
        colorWarning: colour.warningText,
        colorError: colour.dangerText,
        colorInfo: colour.infoText,
      },
      // A status tag draws its text in the token's colour, so it takes the text colour that passes contrast.
      Tag: {
        colorSuccess: colour.successText,
        colorWarning: colour.warningText,
        colorError: colour.dangerText,
        colorInfo: colour.infoText,
      },
      // A default button hovered or pressed draws its text in the text colour that passes contrast, not in a shade of the seed.
      Button: {
        defaultShadow: 'none',
        primaryShadow: 'none',
        dangerShadow: 'none',
        defaultHoverColor: colour.primaryText,
        defaultHoverBorderColor: colour.primaryText,
        defaultActiveColor: colour.primaryText,
        defaultActiveBorderColor: colour.primaryText,
      },
    },
  }
}

/**
 * The props every Axis `ConfigProvider` gets, see docs/reference/actions-and-controls.md#dialogs: the theme; every
 * dialog centred, no taller or wider than the window less its `spacing-xl` margins, with a scrolling body; a Cancel of
 * text type for every dialog, so it is a text button beside the solid OK; and checkboxes and radios that sit at the top
 * of their box, level with the first line of a label that wraps.
 */
export function antConfigFor(mode: ThemeMode, page?: PageTokenValues, reducedMotion = false): ConfigProviderProps {
  const config = antThemeFor(mode, page, reducedMotion)
  const token = theme.getDesignToken(config)
  const { spacing } = designTokensFor(mode, page)
  // Ant Design centres a box on the label's line. Half the gap between the line and the box puts its top on that line.
  const firstLineOffset = (boxSize: number) => (token.fontSize * token.lineHeight - boxSize) / 2
  const margins = 2 * spacing.xl
  return {
    theme: config,
    modal: {
      cancelButtonProps: { type: 'text' },
      centered: true,
      style: { maxWidth: `calc(100vw - ${margins}px)` },
      // The padding sits in the header, body and footer, not the frame, so the body's scrollbar runs at the dialog's edge.
      styles: {
        container: { display: 'flex', flexDirection: 'column', maxHeight: `calc(100vh - ${margins}px)`, padding: 0 },
        header: { padding: `${spacing.lg}px ${spacing.lg}px 0` },
        body: { flex: '1 1 auto', minHeight: 0, overflowY: 'auto', padding: `${spacing.xs}px ${spacing.lg}px` },
        footer: { padding: `0 ${spacing.lg}px ${spacing.lg}px` },
      },
    },
    checkbox: {
      styles: { icon: { alignSelf: 'flex-start', marginTop: firstLineOffset(token.controlInteractiveSize) } },
    },
    radio: { styles: { icon: { alignSelf: 'flex-start', marginTop: firstLineOffset(token.fontSizeLG) } } },
  }
}

/**
 * The CSS every Axis form shares, rendered once beside each provider's ConfigProvider. Ant Design has no token for the
 * gap under a message or for an unticked box's error border, so these rules carry them. The border takes the mode's
 * text colour: a page with two modes would give them one colour.
 */
export function fieldRules(mode: ThemeMode, page?: PageTokenValues): string {
  const { colour, type } = designTokensFor(mode, page)
  return [
    // A message sits 4px under its control. The item keeps its own bottom margin under the message.
    `.ant-form-item .ant-form-item-explain { margin-top: 4px; font-size: ${type.fontSize}px; line-height: 20px; }`,
    '.ant-form-item-margin-offset { display: none; }',
    `.ant-form-item-has-error .ant-checkbox:not(.ant-checkbox-checked), .ant-form-item-has-error .ant-radio:not(.ant-radio-checked) { border-color: ${colour.dangerText}; }`,
  ].join('\n')
}

/** The tint Ant Design gives a selected item. Dropdown menus draw their selected item on it, so Axis reads it here. */
export function selectedItemTint(mode: ThemeMode): string {
  return theme.getDesignToken(antThemeFor(mode)).controlItemBgActive
}

/** Table cells hold form items that need no space below them, as the table rows already space the inputs. */
export const compactFormTheme: ThemeConfig = { components: { Form: { itemMarginBottom: 0 } } }

/**
 * The cell padding of a table, see docs/reference/actions-and-controls.md#tables. A row is `spacing-xs` above and below
 * a line of the `smallControl` size, which a line of text and a small icon button both fill, and `spacing-lg` on each
 * side keeps columns apart. `AxisTable` applies it through a nested ConfigProvider.
 */
export function tableTheme(tokens: DesignTokens): ThemeConfig {
  return { components: { Table: { cellPaddingBlock: tokens.spacing.xs, cellPaddingInline: tokens.spacing.lg } } }
}
