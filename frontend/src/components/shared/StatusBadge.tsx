import type { ComponentProps } from 'react';

import { Badge } from '@/components/ui/badge';

type StatusBadgeTone = 'success' | 'neutral' | 'muted';

const toneStyles = {
  success: {
    variant: 'outline',
    className: 'border-success/25 bg-success/10 text-success',
  },
  neutral: {
    variant: 'secondary',
    className: undefined,
  },
  muted: {
    variant: 'outline',
    className: 'bg-muted/50 text-muted-foreground',
  },
} as const;

interface StatusBadgeProps extends Omit<ComponentProps<typeof Badge>, 'className' | 'variant'> {
  tone: StatusBadgeTone;
}

function StatusBadge({ tone, ...props }: StatusBadgeProps) {
  const style = toneStyles[tone];
  return <Badge {...props} variant={style.variant} className={style.className} />;
}

export { StatusBadge, type StatusBadgeTone };
