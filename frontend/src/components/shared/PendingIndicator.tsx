import type { ReactNode } from 'react';
import { Spinner } from '@/components/ui/spinner';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

interface PendingIndicatorProps {
  children: ReactNode;
  className?: string;
}

export function PendingIndicator({ children, className }: PendingIndicatorProps) {
  return (
    <p
      className={cn(
        'flex min-h-5 items-center text-muted-foreground',
        axisStyles.spacing.gap.inline,
        axisStyles.typography.scale.body,
        axisStyles.typography.weight.body,
        className,
      )}
      role="status"
    >
      <Spinner aria-hidden />
      {children}
    </p>
  );
}
