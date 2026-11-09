// WCAG 2.x contrast for hex, rgb() and rgba() colours. A translucent colour is blended over what lies under it.

interface Rgba {
  red: number
  green: number
  blue: number
  alpha: number
}

const white: Rgba = { red: 255, green: 255, blue: 255, alpha: 1 }

function parseColour(value: string): Rgba {
  const hex = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(value)
  if (hex) return { red: parseInt(hex[1], 16), green: parseInt(hex[2], 16), blue: parseInt(hex[3], 16), alpha: 1 }
  const rgba = /^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/.exec(value)
  if (rgba)
    return {
      red: Number(rgba[1]),
      green: Number(rgba[2]),
      blue: Number(rgba[3]),
      alpha: rgba[4] === undefined ? 1 : Number(rgba[4]),
    }
  throw new Error(`Cannot parse the colour ${value}`)
}

function blend(top: Rgba, bottom: Rgba): Rgba {
  const alpha = top.alpha + bottom.alpha * (1 - top.alpha)
  if (alpha === 0) return { red: 0, green: 0, blue: 0, alpha: 0 }
  const mix = (topChannel: number, bottomChannel: number) =>
    (topChannel * top.alpha + bottomChannel * bottom.alpha * (1 - top.alpha)) / alpha
  return {
    red: mix(top.red, bottom.red),
    green: mix(top.green, bottom.green),
    blue: mix(top.blue, bottom.blue),
    alpha,
  }
}

function luminance({ red, green, blue }: Rgba): number {
  const linear = (channel: number) => {
    const value = channel / 255
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * linear(red) + 0.7152 * linear(green) + 0.0722 * linear(blue)
}

// The contrast from 1 to 21 of a foreground drawn over a background, which is opaque.
export function contrast(foreground: string, background: string): number {
  const under = parseColour(background)
  const shown = luminance(blend(parseColour(foreground), under))
  const base = luminance(under)
  return (Math.max(shown, base) + 0.05) / (Math.min(shown, base) + 0.05)
}

// The opaque rgb() colour behind layers given nearest first. The page's white is the bottom when no layer is opaque.
export function backgroundOf(layers: readonly string[]): string {
  const { red, green, blue } = layers.reduceRight((below, layer) => blend(parseColour(layer), below), white)
  return `rgb(${red}, ${green}, ${blue})`
}

// Large text is 24 px, or 18.66 px and bold (weight 700 or more).
export function isLargeText(fontSize: number, fontWeight: number): boolean {
  return fontSize >= 24 || (fontSize >= 18.66 && fontWeight >= 700)
}

// The ratio WCAG asks for: 4.5 for text, 3 for large text, and 3 for a control's border or an icon.
export function requiredRatio(kind: 'text' | 'border' | 'icon', large: boolean): number {
  if (kind === 'text') return large ? 3 : 4.5
  return 3
}
