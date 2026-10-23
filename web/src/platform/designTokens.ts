import type { ThemeMode } from './themeMode'

// The design system's source (D57). Every colour, type, spacing and radius value of the theme is here.

export interface ColourValue {
  light: string
  dark: string
}

/**
 * Colours with Axis names. Primary, success, warning, danger and info are seed colours: Ant Design derives
 * their dark shade from the light value, so antTheme.ts passes the light value as the seed. The dark value
 * is the shade that results, which components read through useDesignTokens.
 */
export const colourTokens = {
  primary: { light: '#2f54eb', dark: '#2b4acb' },
  success: { light: '#52c41a', dark: '#49aa19' },
  warning: { light: '#faad14', dark: '#d89614' },
  danger: { light: '#ff4d4f', dark: '#dc4446' },
  info: { light: '#1677ff', dark: '#1668dc' },
  canvas: { light: '#f5f5f5', dark: '#000000' },
  surface: { light: '#ffffff', dark: '#141414' },
  text: { light: 'rgba(0,0,0,0.88)', dark: 'rgba(255,255,255,0.85)' },
  textMuted: { light: 'rgba(0,0,0,0.45)', dark: 'rgba(255,255,255,0.45)' },
  border: { light: '#d9d9d9', dark: '#424242' },
  borderSubtle: { light: '#f0f0f0', dark: '#303030' },
  divider: { light: 'rgba(5,5,5,0.06)', dark: 'rgba(253,253,253,0.12)' },
} as const satisfies Record<string, ColourValue>

export const typeTokens = {
  fontFamily: "'Inter', system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif",
  codeFontFamily: "'SFMono-Regular', Consolas, 'Liberation Mono', Menlo, Courier, monospace",
  fontSize: 14,
}

export const spacingTokens = { xs: 8, sm: 12, md: 16, lg: 24, xl: 32 }

export const radiusTokens = { base: 6 }

export type ColourName = keyof typeof colourTokens

export interface DesignTokens {
  colour: Record<ColourName, string>
  type: typeof typeTokens
  spacing: typeof spacingTokens
  radius: typeof radiusTokens
}

/** The tokens of one mode, with each colour resolved to its light or dark value. */
export function designTokensFor(mode: ThemeMode): DesignTokens {
  const colour = Object.fromEntries(Object.entries(colourTokens).map(([name, value]) => [name, value[mode]])) as Record<
    ColourName,
    string
  >
  return { colour, type: typeTokens, spacing: spacingTokens, radius: radiusTokens }
}
