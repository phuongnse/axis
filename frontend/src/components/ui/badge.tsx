import { cva, type VariantProps } from 'class-variance-authority';
import type { HTMLAttributes } from 'react';

import { cn } from '@/lib/utils';

const badgeVariants = cva(
  'inline-flex min-h-6 items-center gap-1 rounded-md border px-2 py-1 text-xs font-medium leading-none',
  {
    variants: {
      variant: {
        neutral: 'border-border bg-muted text-muted-foreground',
        primary: 'border-primary/20 bg-primary/10 text-primary',
        accent: 'border-accent/25 bg-accent/10 text-accent',
        info: 'border-state-info-border bg-state-info-background text-state-info-foreground dark:text-state-info-foreground',
        success:
          'border-state-success-border bg-state-success-background text-state-success-foreground dark:text-state-success-foreground',
        warning:
          'border-state-warning-border bg-state-warning-background text-state-warning-foreground dark:text-state-warning-foreground',
        destructive: 'border-destructive/30 bg-destructive/10 text-destructive',
        outline: 'border-border bg-background text-foreground',
      },
    },
    defaultVariants: {
      variant: 'neutral',
    },
  },
);

interface BadgeProps extends HTMLAttributes<HTMLSpanElement>, VariantProps<typeof badgeVariants> {}

function Badge({ className, variant, ...props }: BadgeProps) {
  return <span className={cn(badgeVariants({ variant }), className)} {...props} />;
}

export { Badge, badgeVariants };
