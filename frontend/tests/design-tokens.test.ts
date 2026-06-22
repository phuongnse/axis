import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import {
  axisBreakpointTokens,
  axisMotionTokens,
  axisRadiusTokens,
  axisSemanticColorTokens,
  axisShadowTokens,
  axisSizingTokens,
  axisSpacingTokens,
  axisTailwindColorTokens,
  axisTailwindRadiusTokens,
  axisTypographyTokens,
} from '../src/design-system/tokens';
import tailwindConfig from '../tailwind.config.js';

type TokenTree = string | { readonly [key: string]: TokenTree };
type TailwindExtend = {
  colors?: unknown;
  borderRadius?: unknown;
};
type TailwindConfig = {
  theme?: {
    extend?: TailwindExtend;
  };
};

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const tokensCss = fs.readFileSync(path.join(frontendRoot, 'src/design-system/tokens.css'), 'utf8');
const tailwindExtend = (tailwindConfig as TailwindConfig).theme?.extend;

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
});
