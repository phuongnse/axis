import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  axisBreakpointTokens,
  axisGradientTokens,
  axisMotionTokens,
  axisRadiusTokens,
  axisSemanticColorTokens,
  axisShadowTokens,
  axisSizingTokens,
  axisSpacingTokens,
  axisTailwindBackgroundImageTokens,
  axisTailwindColorTokens,
  axisTailwindRadiusTokens,
  axisTailwindShadowTokens,
  axisTypographyTokens,
} from '../src/design-system/tokens';
import tailwindConfig from '../tailwind.config.js';

type TokenTree = string | { readonly [key: string]: TokenTree };
type TailwindExtend = {
  backgroundImage?: unknown;
  boxShadow?: unknown;
  colors?: unknown;
  borderRadius?: unknown;
};
type TailwindConfig = {
  theme?: {
    extend?: TailwindExtend;
  };
};
type TokenPair = readonly [sourceToken: string, frontendToken: string];

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const tokensCss = fs.readFileSync(path.join(frontendRoot, 'src/design-system/tokens.css'), 'utf8');
const openDesignTokensCss = fs.readFileSync(
  path.join(frontendRoot, '..', 'design-sources/open-design/axis/tokens.css'),
  'utf8',
);
const tailwindExtend = (tailwindConfig as TailwindConfig).theme?.extend;

const openDesignLightTokenPairs = [
  ['--background', '--background'],
  ['--foreground', '--foreground'],
  ['--surface', '--card'],
  ['--surface', '--popover'],
  ['--surface-muted', '--muted'],
  ['--text-muted', '--muted-foreground'],
  ['--border', '--border'],
  ['--primary', '--primary'],
  ['--primary-foreground', '--primary-foreground'],
  ['--accent', '--accent'],
  ['--accent-foreground', '--accent-foreground'],
  ['--danger', '--destructive'],
  ['--success', '--state-success'],
  ['--warning', '--state-warning'],
  ['--info', '--state-info'],
  ['--radius-panel', '--radius'],
  ['--space-4', '--space-form-gap'],
  ['--space-6', '--space-section-gap'],
  ['--space-8', '--space-page-padding'],
  ['--size-control-sm', '--size-control-sm'],
  ['--size-control-md', '--size-control-md'],
  ['--size-control-lg', '--size-control-lg'],
  ['--shadow-surface', '--shadow-surface'],
  ['--shadow-panel', '--shadow-panel'],
  ['--motion-duration-fast', '--motion-duration-fast'],
  ['--motion-duration-standard', '--motion-duration-standard'],
] as const satisfies readonly TokenPair[];

const openDesignDarkTokenPairs = [
  ['--background', '--background'],
  ['--foreground', '--foreground'],
  ['--surface', '--card'],
  ['--surface', '--popover'],
  ['--surface-muted', '--muted'],
  ['--text-muted', '--muted-foreground'],
  ['--border', '--border'],
  ['--primary', '--primary'],
  ['--accent', '--accent'],
  ['--danger', '--destructive'],
  ['--success', '--state-success'],
  ['--warning', '--state-warning'],
  ['--info', '--state-info'],
  ['--shadow-surface', '--shadow-surface'],
  ['--shadow-panel', '--shadow-panel'],
] as const satisfies readonly TokenPair[];

if (!tailwindExtend) {
  throw new Error('Tailwind theme.extend is required for Axis design tokens.');
}

function cssBlock(selector: string) {
  const match = new RegExp(
    `${escapeRegExp(selector)}\\s*\\{(?<body>[\\s\\S]*?)\\n\\s*\\}`,
    'm',
  ).exec(tokensCss);

  if (!match?.groups?.body) {
    throw new Error(`${selector} block is required in design tokens CSS.`);
  }

  return match.groups.body;
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function cssDeclarations(sourceCss: string, selector: string) {
  const declarations = new Map<string, string>();
  const declarationPattern = /(?<name>--[A-Za-z0-9-]+)\s*:\s*(?<value>[^;]+);/g;

  for (const match of cssBlockFrom(sourceCss, selector).matchAll(declarationPattern)) {
    if (!match.groups?.name || !match.groups.value) {
      continue;
    }

    declarations.set(match.groups.name, match.groups.value.trim());
  }

  return declarations;
}

function cssBlockFrom(sourceCss: string, selector: string) {
  const match = new RegExp(
    `${escapeRegExp(selector)}\\s*\\{(?<body>[\\s\\S]*?)\\n\\s*\\}`,
    'm',
  ).exec(sourceCss);

  if (!match?.groups?.body) {
    throw new Error(`${selector} block is required in design tokens CSS.`);
  }

  return match.groups.body;
}

function flattenTokenTree(
  tree: TokenTree,
  tokenPath: readonly string[] = [],
): Array<{ tokenPath: readonly string[]; cssVariable: string }> {
  if (typeof tree === 'string') {
    return [{ tokenPath, cssVariable: tree }];
  }

  return Object.entries(tree).flatMap(([key, value]) =>
    flattenTokenTree(value, [...tokenPath, key]),
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function readPath(root: unknown, tokenPath: readonly string[]) {
  let current = root;

  for (const segment of tokenPath) {
    if (!isRecord(current)) {
      return undefined;
    }

    current = current[segment];
  }

  return current;
}

function declarationValue(
  declarations: ReadonlyMap<string, string>,
  token: string,
  selector: string,
) {
  const value = declarations.get(token);

  if (!value) {
    throw new Error(`${selector} is missing ${token}.`);
  }

  return normalizeTokenValue(value);
}

function normalizeTokenValue(value: string) {
  const normalized = value.trim().replace(/\s+/g, ' ');
  const hsl = /^hsl\((?<channels>.*)\)$/.exec(normalized);
  const rem = /^(?<amount>\d+(?:\.\d+)?)rem$/.exec(hsl?.groups?.channels ?? normalized);

  if (rem?.groups?.amount) {
    return `${Number(rem.groups.amount) * 16}px`;
  }

  return hsl?.groups?.channels ?? normalized;
}

describe('Axis design tokens', () => {
  it('declares every semantic color token in light and dark themes', () => {
    const lightTokens = cssBlock(':root');
    const darkTokens = cssBlock('.dark');

    for (const token of axisSemanticColorTokens) {
      expect(lightTokens).toContain(`${token}:`);
      expect(darkTokens).toContain(`${token}:`);
    }
  });

  it('declares shape and typography source tokens', () => {
    const lightTokens = cssBlock(':root');
    const themeTokens = cssBlock('.theme');

    for (const token of [
      ...axisRadiusTokens,
      ...axisSpacingTokens,
      ...axisSizingTokens,
      ...axisShadowTokens,
      ...axisGradientTokens,
      ...axisMotionTokens,
      ...axisBreakpointTokens,
    ]) {
      expect(lightTokens).toContain(`${token}:`);
    }

    for (const token of axisTypographyTokens) {
      expect(themeTokens).toContain(`${token}:`);
    }
  });

  it('maps Tailwind color names to the semantic CSS variables', () => {
    for (const { tokenPath, cssVariable } of flattenTokenTree(axisTailwindColorTokens)) {
      expect(readPath(tailwindExtend.colors, tokenPath)).toBe(`hsl(var(${cssVariable}))`);
    }
  });

  it('maps Tailwind radius names to the shared radius token', () => {
    for (const [radiusName, tokenValue] of Object.entries(axisTailwindRadiusTokens)) {
      expect(readPath(tailwindExtend.borderRadius, [radiusName])).toBe(tokenValue);
    }
  });

  it('maps Tailwind shadow names to shadow tokens', () => {
    for (const [shadowName, tokenValue] of Object.entries(axisTailwindShadowTokens)) {
      expect(readPath(tailwindExtend.boxShadow, [shadowName])).toBe(tokenValue);
    }
  });

  it('maps Tailwind background image names to gradient tokens', () => {
    for (const [backgroundName, tokenValue] of Object.entries(axisTailwindBackgroundImageTokens)) {
      expect(readPath(tailwindExtend.backgroundImage, [backgroundName])).toBe(tokenValue);
    }
  });

  it('keeps executable tokens aligned with the Open Design seed', () => {
    const frontendLightTokens = cssDeclarations(tokensCss, ':root');
    const frontendDarkTokens = cssDeclarations(tokensCss, '.dark');
    const sourceLightTokens = cssDeclarations(openDesignTokensCss, ':root');
    const sourceDarkTokens = cssDeclarations(openDesignTokensCss, '.dark');

    for (const [sourceToken, frontendToken] of openDesignLightTokenPairs) {
      expect(declarationValue(frontendLightTokens, frontendToken, ':root')).toBe(
        declarationValue(sourceLightTokens, sourceToken, 'Open Design :root'),
      );
    }

    for (const [sourceToken, frontendToken] of openDesignDarkTokenPairs) {
      expect(declarationValue(frontendDarkTokens, frontendToken, '.dark')).toBe(
        declarationValue(sourceDarkTokens, sourceToken, 'Open Design .dark'),
      );
    }
  });
});
