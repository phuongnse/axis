// The WCAG contrast of what a widget preview draws. The browser reads the colours and the font, and this module
// does the arithmetic, so it can be unit tested without a browser.

export interface Rgba {
  red: number;
  green: number;
  blue: number;
  alpha: number;
}

const white: Rgba = { red: 255, green: 255, blue: 255, alpha: 1 };

/** A colour as getComputedStyle writes it: rgb() or rgba(), with channels from 0 to 255 and alpha from 0 to 1. */
export function parseColour(css: string): Rgba {
  const match =
    /^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*(?:,\s*([\d.]+)\s*)?\)$/.exec(
      css.trim(),
    );
  if (!match) {
    throw new Error(`Unknown colour format: ${css}`);
  }
  const [red, green, blue, alpha] = match
    .slice(1, 5)
    .map((channel) => (channel === undefined ? 1 : Number(channel)));
  return { red, green, blue, alpha };
}

/** Places `top` over `bottom` with the source-over rule of CSS compositing. */
export function blend(top: Rgba, bottom: Rgba): Rgba {
  const alpha = top.alpha + bottom.alpha * (1 - top.alpha);
  if (alpha === 0) {
    return { red: 0, green: 0, blue: 0, alpha: 0 };
  }
  const mix = (topChannel: number, bottomChannel: number) =>
    (topChannel * top.alpha + bottomChannel * bottom.alpha * (1 - top.alpha)) /
    alpha;
  return {
    red: mix(top.red, bottom.red),
    green: mix(top.green, bottom.green),
    blue: mix(top.blue, bottom.blue),
    alpha,
  };
}

/**
 * The opaque colour behind a stack of background layers, given nearest first. The layers below the first opaque one
 * are hidden, so the stack starts there. With no opaque layer, the page's white is the bottom.
 */
export function backgroundOf(layers: readonly string[]): Rgba {
  const parsed = layers.map(parseColour);
  const opaque = parsed.findIndex((layer) => layer.alpha === 1);
  let base = opaque === -1 ? white : parsed[opaque];
  for (
    let index = (opaque === -1 ? parsed.length : opaque) - 1;
    index >= 0;
    index--
  ) {
    base = blend(parsed[index], base);
  }
  return base;
}

function luminance({ red, green, blue }: Rgba): number {
  const linear = (channel: number) => {
    const value = channel / 255;
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * linear(red) + 0.7152 * linear(green) + 0.0722 * linear(blue);
}

/** The WCAG ratio of a foreground over an opaque background, from 1 to 21. */
export function contrastRatio(foreground: Rgba, background: Rgba): number {
  const shown = blend(foreground, background);
  const [lighter, darker] = [luminance(shown), luminance(background)].sort(
    (a, b) => b - a,
  );
  return (lighter + 0.05) / (darker + 0.05);
}

/** Large text is 24 px, or 18.66 px and bold (weight 700 or more). */
export function isLargeText(fontSize: number, fontWeight: number): boolean {
  return fontSize >= 24 || (fontSize >= 18.66 && fontWeight >= 700);
}

/** The ratio WCAG asks for: 4.5 for text, 3 for large text, and 3 for a control's border or an icon. */
export function requiredRatio(
  kind: "text" | "border" | "icon",
  large: boolean,
): number {
  if (kind === "text") {
    return large ? 3 : 4.5;
  }
  return 3;
}
