import type { ReactNode } from 'react';
import { Spinner } from '@/components/ui/spinner';
import { cn } from '@/lib/utils';

interface PendingIndicatorProps {
  children: ReactNode;
  className?: string;
}

export function PendingIndicator({ children, className }: PendingIndicatorProps) {
  return (
    <p
      className={cn(
        'flex min-h-5 items-center gap-axis-inline text-axis-body font-axis-body text-muted-foreground',
        className,
      )}
      role="status"
    >
      <Spinner aria-hidden />
      {children}
    </p>
  );
}
