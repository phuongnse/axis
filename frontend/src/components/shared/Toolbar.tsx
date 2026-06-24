import type { HTMLAttributes } from 'react';

import { cn } from '@/lib/utils';

function Toolbar({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn(
        'flex min-h-10 flex-wrap items-center gap-2 rounded-lg border border-border bg-card p-2 shadow-surface',
        className,
      )}
      {...props}
    />
  );
}

export { Toolbar };
