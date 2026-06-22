import { cva, type VariantProps } from 'class-variance-authority';
import type { HTMLAttributes } from 'react';

import { cn } from '@/lib/utils';

const panelVariants = cva('rounded-lg border p-5', {
  variants: {
    variant: {
      default: 'border-border bg-card text-card-foreground shadow-sm',
      muted: 'border-border bg-muted/30 text-foreground',
      inset: 'border-border bg-background/80 text-foreground',
      attention: 'border-destructive/30 bg-card text-card-foreground shadow-sm',
      inverse:
        'border-inverse-border bg-inverse text-inverse-foreground shadow-[var(--shadow-panel)]',
    },
  },
  defaultVariants: {
    variant: 'default',
  },
});

interface PanelProps extends HTMLAttributes<HTMLDivElement>, VariantProps<typeof panelVariants> {}

function Panel({ className, variant, ...props }: PanelProps) {
  return <div className={cn(panelVariants({ variant }), className)} {...props} />;
}

export { Panel, panelVariants };
