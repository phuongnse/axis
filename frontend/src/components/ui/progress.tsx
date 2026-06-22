import type { ProgressHTMLAttributes } from 'react';

import { cn } from '@/lib/utils';

interface ProgressProps extends ProgressHTMLAttributes<HTMLProgressElement> {
  isIndeterminate?: boolean;
}

function Progress({
  className,
  isIndeterminate = false,
  value,
  max = 100,
  ...props
}: ProgressProps) {
  return (
    <progress
      className={cn(
        'h-1.5 w-full overflow-hidden rounded-full bg-muted accent-primary',
        '[&::-moz-progress-bar]:rounded-full [&::-moz-progress-bar]:bg-primary',
        '[&::-webkit-progress-bar]:rounded-full [&::-webkit-progress-bar]:bg-muted',
        '[&::-webkit-progress-value]:rounded-full [&::-webkit-progress-value]:bg-primary',
        isIndeterminate && 'opacity-40',
        className,
      )}
      value={isIndeterminate ? undefined : value}
      max={max}
      {...props}
    />
  );
}

export { Progress };
