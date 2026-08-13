import type { ComponentProps } from 'react';

import { Badge } from '@/components/ui/badge';

type StatusBadgeState =
  | 'informative'
  | 'positive'
  | 'caution'
  | 'critical'
  | 'neutral'
  | 'inactive';

const stateStyles = {
  informative: {
    variant: 'outline',
    className: 'border-info/25 bg-info/10 text-info',
  },
  positive: {
    variant: 'outline',
    className: 'border-success/25 bg-success/10 text-success',
  },
  caution: {
    variant: 'outline',
    className: 'border-warning/25 bg-warning/10 text-warning',
  },
  critical: {
    variant: 'outline',
    className: 'border-destructive/25 bg-destructive/10 text-destructive',
  },
  neutral: {
    variant: 'secondary',
    className: undefined,
  },
  inactive: {
    variant: 'outline',
    className: 'bg-muted/50 text-muted-foreground',
  },
} as const;

interface StatusBadgeProps extends Omit<ComponentProps<typeof Badge>, 'className' | 'variant'> {
  state: StatusBadgeState;
}

function StatusBadge({ state, ...props }: StatusBadgeProps) {
  const style = stateStyles[state];
  return (
    <Badge
      {...props}
      data-status-state={state}
      variant={style.variant}
      className={style.className}
    />
  );
}

export { StatusBadge, type StatusBadgeState };
