import assert from 'node:assert/strict';
import test from 'node:test';

import { findAxisStyleConsumptionIssues } from './check-axis-style-consumption.mjs';

test('allows typed Axis style consumption', () => {
  const issues = findAxisStyleConsumptionIssues([
    {
      path: 'src/components/shared/Example.tsx',
      source: `
        import { axisStyles } from '@/theme.generated';

        export function Example() {
          return <div className={axisStyles.spacing.gap.inline} />;
        }
      `,
    },
  ]);

  assert.deepEqual(issues, []);
});

test('rejects raw Axis style utilities in authored source and tests', () => {
  const issues = findAxisStyleConsumptionIssues([
    {
      path: 'src/components/shared/Example.tsx',
      source: `export const classes = \`grid gap-axis-inline ${'$'}{conditional}\`;`,
    },
    {
      path: 'tests/example.test.tsx',
      source: "expect(element).toHaveClass('sm:min-h-axis-compact-control');",
    },
  ]);

  assert.deepEqual(
    issues.map(({ path, token }) => ({ path, token })),
    [
      { path: 'src/components/shared/Example.tsx', token: 'gap-axis-inline' },
      { path: 'tests/example.test.tsx', token: 'sm:min-h-axis-compact-control' },
    ],
  );
});

test('rejects slash modifiers on raw Axis style utilities', () => {
  const issues = findAxisStyleConsumptionIssues([
    {
      path: 'src/components/shared/Example.tsx',
      source: 'export const classes = "hover:text-axis-body/50 bg-axis-surface/50";',
    },
  ]);

  assert.deepEqual(
    issues.map(({ token }) => token),
    ['hover:text-axis-body/50', 'bg-axis-surface/50'],
  );
});

test('allows data-axis attributes and selectors', () => {
  const issues = findAxisStyleConsumptionIssues([
    {
      path: 'src/lib/surface.tsx',
      source: `
        export const marker = 'data-axis-surface-contract';
        export const selector = '[data-axis-surface-id="account-actions"]';
        export const view = <div data-axis-account-region="identity" />;
      `,
    },
  ]);

  assert.deepEqual(issues, []);
});

test('allows Axis reference, domain, and asset identifiers', () => {
  const issues = findAxisStyleConsumptionIssues([
    {
      path: 'tests/reference.test.ts',
      source: `
        export const product = 'axis-reference-product';
        export const endpoint = 'https://axis-reference.example/assets/axis-reference-logo.svg';
        export const domain = 'https://service-axis.example/reference';
        export const asset = 'brand-axis-logo.svg';
        export const modulePath = '@/features/brand-axis-reference';
      `,
    },
  ]);

  assert.deepEqual(issues, []);
});

test('excludes generated API and route sources plus upstream UI components', () => {
  const issues = findAxisStyleConsumptionIssues([
    {
      path: 'src/lib/api-generated/types.gen.ts',
      source: 'export const value = "gap-axis-inline";',
    },
    { path: 'src/routeTree.gen.ts', source: 'export const value = "gap-axis-inline";' },
    { path: 'src/theme.generated.ts', source: 'export const value = "gap-axis-inline";' },
    {
      path: 'src/components/ui/button.tsx',
      source: 'export const value = "gap-axis-inline";',
    },
  ]);

  assert.deepEqual(issues, []);
});
