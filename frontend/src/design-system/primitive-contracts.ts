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
    component: 'Badge',
    file: 'frontend/src/components/ui/badge.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/badge',
    readiness: 'ready',
    variants: ['default', 'secondary', 'destructive', 'outline', 'ghost', 'link'],
    states: ['default'],
    accessibility: ['text-accessible-name'],
    tokenFamilies: ['color', 'border', 'radius', 'sizing'],
  },
  {
    component: 'Button',
    file: 'frontend/src/components/ui/button.tsx',
    testFiles: ['frontend/tests/button.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/button',
    readiness: 'ready',
    variants: [
      'default',
      'outline',
      'secondary',
      'ghost',
      'destructive',
      'link',
      'size-xs',
      'size-sm',
      'size-default',
      'size-lg',
      'size-icon',
      'size-icon-xs',
      'size-icon-sm',
      'size-icon-lg',
    ],
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
    variants: ['default', 'size-sm', 'header', 'content', 'footer', 'action'],
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
    component: 'Empty',
    file: 'frontend/src/components/ui/empty.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/empty',
    readiness: 'ready',
    variants: ['default', 'media-default', 'media-icon', 'content'],
    states: ['empty'],
    accessibility: ['semantic-caller-owned', 'description-slot'],
    tokenFamilies: ['color', 'border', 'radius', 'spacing'],
  },
  {
    component: 'Field',
    file: 'frontend/src/components/ui/field.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/field',
    readiness: 'ready',
    variants: ['vertical', 'horizontal', 'responsive', 'fieldset'],
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
    component: 'NativeSelect',
    file: 'frontend/src/components/ui/native-select.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/native-select',
    readiness: 'ready',
    variants: ['default', 'size-sm', 'option', 'optgroup'],
    states: ['default', 'disabled', 'invalid', 'described'],
    accessibility: ['native-select', 'label-or-aria-label', 'aria-invalid'],
    tokenFamilies: ['color', 'border', 'radius', 'sizing'],
  },
  {
    component: 'Progress',
    file: 'frontend/src/components/ui/progress.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/progress',
    readiness: 'ready',
    variants: ['determinate', 'indeterminate', 'label', 'value'],
    states: ['in-progress', 'complete'],
    accessibility: ['base-ui-progressbar', 'accessible-label'],
    tokenFamilies: ['color', 'radius', 'sizing'],
  },
  {
    component: 'Separator',
    file: 'frontend/src/components/ui/separator.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/separator',
    readiness: 'ready',
    variants: ['horizontal', 'vertical'],
    states: ['default'],
    accessibility: ['base-ui-separator'],
    tokenFamilies: ['color', 'sizing'],
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
  {
    component: 'Spinner',
    file: 'frontend/src/components/ui/spinner.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/spinner',
    readiness: 'ready',
    variants: ['default'],
    states: ['loading'],
    accessibility: ['role-status', 'aria-label-loading'],
    tokenFamilies: ['sizing', 'motion'],
  },
  {
    component: 'Textarea',
    file: 'frontend/src/components/ui/textarea.tsx',
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
    source: 'shadcn',
    sourceItem: '@shadcn/textarea',
    readiness: 'ready',
    variants: ['default'],
    states: ['default', 'disabled', 'invalid', 'described'],
    accessibility: ['label-or-aria-label', 'aria-invalid', 'aria-describedby'],
    tokenFamilies: ['color', 'border', 'radius', 'sizing'],
  },
] as const satisfies readonly AxisPrimitiveContract[];
