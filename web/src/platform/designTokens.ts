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
  spacing: Record<keyof typeof spacingTokens, number>
  radius: Record<keyof typeof radiusTokens, number>
}

type SpacingName = keyof typeof spacingTokens

/**
 * The CSS custom property that carries each Axis token on a page. Only the design-system bundle reads them, from
 * the page's root element. Colours use their Axis name as written.
 */
export const pageTokenProperties = {
  colour: Object.fromEntries(Object.keys(colourTokens).map((name) => [name, `--${name}`])) as Record<
    ColourName,
    string
  >,
  spacing: Object.fromEntries(Object.keys(spacingTokens).map((name) => [name, `--spacing-${name}`])) as Record<
    SpacingName,
    string
  >,
  radius: { base: '--radius-base' },
  type: { fontFamily: '--font-sans', codeFontFamily: '--font-code' },
}

/** Raw property values of a page, keyed by property name. A property the page leaves out is missing or empty. */
export type PageTokenValues = Partial<Record<string, string>>

/** Every property name of pageTokenProperties. */
export const pageTokenPropertyNames: string[] = [
  ...Object.values(pageTokenProperties.colour),
  ...Object.values(pageTokenProperties.spacing),
  ...Object.values(pageTokenProperties.radius),
  ...Object.values(pageTokenProperties.type),
]

// The forms that Ant Design's colour parser and number tokens accept.
const colourPattern = /^(#([0-9a-f]{3,4}|[0-9a-f]{6}|[0-9a-f]{8})|(rgba?|hsla?)\([^()]*\))$/i
const lengthPattern = /^\d+(\.\d+)?(px)?$/

function usableColour(value: string | undefined): string | undefined {
  const text = value?.trim()
  return text !== undefined && colourPattern.test(text) ? text : undefined
}

function usableLength(value: string | undefined): number | undefined {
  const text = value?.trim()
  return text !== undefined && lengthPattern.test(text) ? Number.parseFloat(text) : undefined
}

function usableFont(value: string | undefined): string | undefined {
  const text = value?.trim()
  return text ? text : undefined
}

/**
 * The tokens of one mode, with each colour resolved to its light or dark value. A value the page declares and the
 * theme can use replaces the token file's value. Anything else keeps the token file's value.
 */
export function designTokensFor(mode: ThemeMode, page: PageTokenValues = {}): DesignTokens {
  const colour = Object.fromEntries(
    (Object.keys(colourTokens) as ColourName[]).map((name) => [
      name,
      usableColour(page[pageTokenProperties.colour[name]]) ?? colourTokens[name][mode],
    ]),
  ) as Record<ColourName, string>
  const spacing = Object.fromEntries(
    (Object.keys(spacingTokens) as SpacingName[]).map((name) => [
      name,
      usableLength(page[pageTokenProperties.spacing[name]]) ?? spacingTokens[name],
    ]),
  ) as Record<SpacingName, number>
  return {
    colour,
    type: {
      fontFamily: usableFont(page[pageTokenProperties.type.fontFamily]) ?? typeTokens.fontFamily,
      codeFontFamily: usableFont(page[pageTokenProperties.type.codeFontFamily]) ?? typeTokens.codeFontFamily,
      fontSize: typeTokens.fontSize,
    },
    spacing,
    radius: { base: usableLength(page[pageTokenProperties.radius.base]) ?? radiusTokens.base },
  }
}
