import { theme, type ThemeConfig } from 'antd'
import { colourTokens, designTokensFor } from './designTokens'
import type { ThemeMode } from './themeMode'

// The only file that names Ant Design tokens. It maps the Axis tokens to Ant Design's theme.

/** The theme for one mode. Ant Design keeps its algorithm and derives hover and active colours from it. */
export function antThemeFor(mode: ThemeMode): ThemeConfig {
  const { colour, type, spacing, radius } = designTokensFor(mode)
  return {
    algorithm: mode === 'dark' ? theme.darkAlgorithm : theme.defaultAlgorithm,
    token: {
      // Seed colours: Ant Design derives their shade for the mode from the light value.
      colorPrimary: colourTokens.primary.light,
      colorSuccess: colourTokens.success.light,
      colorWarning: colourTokens.warning.light,
      colorError: colourTokens.danger.light,
      colorInfo: colourTokens.info.light,
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
