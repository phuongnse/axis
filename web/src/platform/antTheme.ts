import { theme, type ThemeConfig } from 'antd'
import { colourTokens, designTokensFor, type ColourName, type PageTokenValues } from './designTokens'
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
      Form: { colorError: colour.dangerText },
      Dropdown: { colorError: colour.dangerText },
      Tabs: {
        itemSelectedColor: colour.primaryText,
        itemHoverColor: colour.primaryText,
        itemActiveColor: colour.primaryText,
      },
      Menu: {
        itemSelectedColor: colour.primaryText,
        horizontalItemSelectedColor: colour.primaryText,
        collapsedWidth: 64,
        dangerItemColor: colour.dangerText,
        dangerItemHoverColor: colour.dangerText,
        dangerItemSelectedColor: colour.dangerText,
      },
      Pagination: { itemActiveColor: colour.primaryText, itemActiveColorHover: colour.primaryText },
      Button: { defaultShadow: 'none', primaryShadow: 'none', dangerShadow: 'none' },
    },
  }
}

/** The tint Ant Design gives a selected item. Dropdown menus draw their selected item on it, so Axis reads it here. */
export function selectedItemTint(mode: ThemeMode): string {
  return theme.getDesignToken(antThemeFor(mode)).controlItemBgActive
}

/** Table cells hold form items that need no space below them, as the table rows already space the inputs. */
export const compactFormTheme: ThemeConfig = { components: { Form: { itemMarginBottom: 0 } } }
