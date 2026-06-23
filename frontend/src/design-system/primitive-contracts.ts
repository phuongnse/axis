export interface AxisPrimitiveContract {
  component: string;
  file: string;
  catalogTargets: readonly string[];
  visualTargets: readonly string[];
  testFiles: readonly string[];
}

export const axisPrimitiveContracts = [
  {
    component: 'ActionLink',
    file: 'frontend/src/components/ui/action-link.tsx',
    catalogTargets: ['primitive-action-link'],
    visualTargets: ['primitive-action-link'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'Badge',
    file: 'frontend/src/components/ui/badge.tsx',
    catalogTargets: ['feedback'],
    visualTargets: ['feedback'],
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
  },
  {
    component: 'Button',
    file: 'frontend/src/components/ui/button.tsx',
    catalogTargets: ['primitive-button'],
    visualTargets: ['primitive-button'],
    testFiles: ['frontend/tests/button.test.tsx'],
  },
  {
    component: 'Card',
    file: 'frontend/src/components/ui/card.tsx',
    catalogTargets: ['structure-data'],
    visualTargets: ['structure-data'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'Checkbox',
    file: 'frontend/src/components/ui/checkbox.tsx',
    catalogTargets: ['primitive-form'],
    visualTargets: ['primitive-form'],
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
  },
  {
    component: 'CheckboxField',
    file: 'frontend/src/components/ui/checkbox-field.tsx',
    catalogTargets: ['primitive-form'],
    visualTargets: ['primitive-form'],
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
  },
  {
    component: 'ContentGrid',
    file: 'frontend/src/components/ui/content-grid.tsx',
    catalogTargets: ['layout'],
    visualTargets: ['layout'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'EmptyState',
    file: 'frontend/src/components/ui/empty-state.tsx',
    catalogTargets: ['structure-data'],
    visualTargets: ['structure-data'],
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
  },
  {
    component: 'FormField',
    file: 'frontend/src/components/ui/form-field.tsx',
    catalogTargets: ['primitive-form'],
    visualTargets: ['primitive-form'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'IconButton',
    file: 'frontend/src/components/ui/icon-button.tsx',
    catalogTargets: ['primitive-icon-button'],
    visualTargets: ['primitive-icon-button'],
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
  },
  {
    component: 'Input',
    file: 'frontend/src/components/ui/input.tsx',
    catalogTargets: ['primitive-form'],
    visualTargets: ['primitive-form'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'Label',
    file: 'frontend/src/components/ui/label.tsx',
    catalogTargets: ['primitive-form'],
    visualTargets: ['primitive-form'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'Notice',
    file: 'frontend/src/components/ui/notice.tsx',
    catalogTargets: ['feedback'],
    visualTargets: ['feedback'],
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
  },
  {
    component: 'PageHeader',
    file: 'frontend/src/components/ui/page-header.tsx',
    catalogTargets: ['layout'],
    visualTargets: ['layout'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'Panel',
    file: 'frontend/src/components/ui/panel.tsx',
    catalogTargets: ['structure-data'],
    visualTargets: ['structure-data'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'Progress',
    file: 'frontend/src/components/ui/progress.tsx',
    catalogTargets: ['feedback'],
    visualTargets: ['feedback'],
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
  },
  {
    component: 'Select',
    file: 'frontend/src/components/ui/select.tsx',
    catalogTargets: ['primitive-form'],
    visualTargets: ['primitive-form'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'Skeleton',
    file: 'frontend/src/components/ui/skeleton.tsx',
    catalogTargets: ['feedback'],
    visualTargets: ['feedback'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
  {
    component: 'Textarea',
    file: 'frontend/src/components/ui/textarea.tsx',
    catalogTargets: ['primitive-form'],
    visualTargets: ['primitive-form'],
    testFiles: ['frontend/tests/ui-primitives.test.tsx'],
  },
  {
    component: 'Toolbar',
    file: 'frontend/src/components/ui/toolbar.tsx',
    catalogTargets: ['layout'],
    visualTargets: ['layout'],
    testFiles: ['frontend/tests/design-system-catalog.test.tsx'],
  },
] as const satisfies readonly AxisPrimitiveContract[];
