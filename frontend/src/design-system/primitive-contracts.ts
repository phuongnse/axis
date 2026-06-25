export type AxisPrimitiveReadiness = 'ready' | 'candidate';

export type AxisPrimitiveTokenFamily =
  | 'border'
  | 'breakpoint'
  | 'color'
  | 'motion'
  | 'radius'
  | 'shadow'
  | 'sizing'
  | 'spacing'
  | 'typography';

export interface AxisPrimitiveContract {
  component: string;
  file: string;
  testFiles: readonly string[];
  source: 'shadcn';
  sourceItem: string;
  readiness: AxisPrimitiveReadiness;
  variants: readonly string[];
  states: readonly string[];
  accessibility: readonly string[];
  tokenFamilies: readonly AxisPrimitiveTokenFamily[];
}

export const axisPrimitiveContracts = [
  {
    component: 'Alert',
    file: 'frontend/src/components/ui/alert.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/alert',
    readiness: 'ready',
    variants: ['default', 'destructive'],
    states: ['default'],
    accessibility: ['role-alert', 'title-description-slots'],
    tokenFamilies: ['color', 'border', 'radius', 'spacing'],
  },
  {
    component: 'Button',
    file: 'frontend/src/components/ui/button.tsx',
    testFiles: ['frontend/tests/button.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/button',
    readiness: 'ready',
    variants: ['default', 'outline', 'link', 'size-default', 'size-sm'],
    states: ['default', 'disabled', 'invalid'],
    accessibility: ['native-button', 'focus-visible', 'disabled-interaction'],
    tokenFamilies: ['color', 'border', 'radius', 'sizing', 'motion'],
  },
  {
    component: 'Card',
    file: 'frontend/src/components/ui/card.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/card',
    readiness: 'ready',
    variants: ['default'],
    states: ['default'],
    accessibility: ['semantic-caller-owned', 'slot-order'],
    tokenFamilies: ['color', 'border', 'radius', 'spacing'],
  },
  {
    component: 'Checkbox',
    file: 'frontend/src/components/ui/checkbox.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/checkbox',
    readiness: 'ready',
    variants: ['unchecked', 'checked'],
    states: ['default', 'checked', 'disabled', 'invalid'],
    accessibility: ['base-ui-checkbox', 'focus-visible', 'aria-invalid'],
    tokenFamilies: ['color', 'border', 'radius'],
  },
  {
    component: 'Field',
    file: 'frontend/src/components/ui/field.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/field',
    readiness: 'ready',
    variants: ['vertical'],
    states: ['default', 'invalid'],
    accessibility: ['label-description-error-slots', 'role-group'],
    tokenFamilies: ['color', 'border', 'radius', 'spacing', 'typography'],
  },
  {
    component: 'Input',
    file: 'frontend/src/components/ui/input.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/input',
    readiness: 'ready',
    variants: ['text'],
    states: ['default', 'disabled', 'invalid', 'described'],
    accessibility: ['label-or-aria-label', 'aria-invalid', 'aria-describedby'],
    tokenFamilies: ['color', 'border', 'radius', 'sizing'],
  },
  {
    component: 'Label',
    file: 'frontend/src/components/ui/label.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/label',
    readiness: 'ready',
    variants: ['default'],
    states: ['default', 'disabled-peer'],
    accessibility: ['html-for-control', 'base-label'],
    tokenFamilies: ['color', 'typography'],
  },
  {
    component: 'Skeleton',
    file: 'frontend/src/components/ui/skeleton.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/skeleton',
    readiness: 'ready',
    variants: ['default'],
    states: ['loading'],
    accessibility: ['decorative-caller-owned'],
    tokenFamilies: ['color', 'radius', 'motion'],
  },
] as const satisfies readonly AxisPrimitiveContract[];
