import { describe, expect, it } from 'vitest';

import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

describe('Axis style composition', () => {
  it('preserves independent typography roles and standard color or family utilities', () => {
    expect(
      cn(
        'font-heading text-foreground',
        axisStyles.typography.scale.pageTitle,
        axisStyles.typography.weight.pageTitle,
      ),
    ).toBe(
      `font-heading text-foreground ${axisStyles.typography.scale.pageTitle} ${axisStyles.typography.weight.pageTitle}`,
    );
  });

  it('resolves Axis roles in their canonical Tailwind groups', () => {
    expect(cn('text-sm', axisStyles.typography.scale.pageTitle)).toBe(
      axisStyles.typography.scale.pageTitle,
    );
    expect(cn('font-normal', axisStyles.typography.weight.label)).toBe(
      axisStyles.typography.weight.label,
    );
    expect(cn('p-2', axisStyles.spacing.padding.all.region)).toBe(
      axisStyles.spacing.padding.all.region,
    );
    expect(cn('rounded-sm', axisStyles.radius.managed)).toBe(axisStyles.radius.managed);
    expect(cn('shadow-sm', axisStyles.elevation.managed)).toBe(axisStyles.elevation.managed);
    expect(cn('z-10', axisStyles.layer.managed)).toBe(axisStyles.layer.managed);
    expect(cn('duration-100', axisStyles.motion.duration.state)).toBe(
      axisStyles.motion.duration.state,
    );
    expect(cn('ease-linear', axisStyles.motion.easing.state)).toBe(axisStyles.motion.easing.state);
  });
});
