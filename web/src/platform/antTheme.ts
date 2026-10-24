import { theme, type ThemeConfig } from 'antd'
import { colourTokens, designTokensFor, type ColourName, type PageTokenValues } from './designTokens'
import type { ThemeMode } from './themeMode'

// The only file that names Ant Design tokens. It maps the Axis tokens to Ant Design's theme.

/**
 * The theme for one mode. Ant Design keeps its algorithm and derives hover and active colours from it. Page values
 * replace the token file's values where the theme can use them.
 */
export function antThemeFor(mode: ThemeMode, page?: PageTokenValues): ThemeConfig {
  const { colour, type, spacing, radius } = designTokensFor(mode, page)
  // Ant Design derives the shade for the mode from a seed. A colour that still equals the token file's value for
  // the mode keeps the light seed, as the dark value is already a shade. Any other colour is the seed itself.
  const seed = (name: ColourName) =>
    colour[name].toLowerCase() === colourTokens[name][mode] ? colourTokens[name].light : colour[name]
  return {
    algorithm: mode === 'dark' ? theme.darkAlgorithm : theme.defaultAlgorithm,
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
    },
  }
}

/** Table cells hold form items that need no space below them, as the table rows already space the inputs. */
export const compactFormTheme: ThemeConfig = { components: { Form: { itemMarginBottom: 0 } } }
