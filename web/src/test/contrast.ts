// WCAG 2.x contrast of a colour over a solid background, for hex colours and rgb or rgba colours.

function channels(value: string): [number, number, number, number] {
  const hex = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(value)
  if (hex) return [parseInt(hex[1], 16), parseInt(hex[2], 16), parseInt(hex[3], 16), 1]
  const rgba = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)$/.exec(value)
  if (rgba) return [Number(rgba[1]), Number(rgba[2]), Number(rgba[3]), rgba[4] === undefined ? 1 : Number(rgba[4])]
  throw new Error(`Cannot parse the colour ${value}`)
}

function over(foreground: string, background: string): [number, number, number] {
  const [r, g, b, a] = channels(foreground)
  const [br, bg, bb] = channels(background)
  return [r * a + br * (1 - a), g * a + bg * (1 - a), b * a + bb * (1 - a)]
}

function luminance([r, g, b]: [number, number, number]): number {
  const linear = (c: number) => {
    const s = c / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * linear(r) + 0.7152 * linear(g) + 0.0722 * linear(b)
}

export function contrast(foreground: string, background: string): number {
  const a = luminance(over(foreground, background))
  const b = luminance(channels(background).slice(0, 3) as [number, number, number])
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}
