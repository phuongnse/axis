import { LoaderCircle } from 'lucide-react';
import type { HTMLAttributes } from 'react';

import { cn } from '@/lib/utils';

type SpinnerSize = 'sm' | 'md' | 'lg';

interface SpinnerProps extends HTMLAttributes<HTMLSpanElement> {
  decorative?: boolean;
  label?: string;
  size?: SpinnerSize;
}

const spinnerSizes = {
  sm: 'size-3.5',
  md: 'size-4',
  lg: 'size-5',
} satisfies Record<SpinnerSize, string>;

function Spinner({
  className,
  decorative = false,
  label = 'Loading',
  size = 'md',
  ...props
}: SpinnerProps) {
  const accessibilityProps = decorative
    ? { 'aria-hidden': true }
    : { 'aria-label': label, 'aria-live': 'polite' as const, role: 'status' };

  return (
    <span
      {...props}
      {...accessibilityProps}
      className={cn('inline-flex shrink-0 items-center justify-center text-primary', className)}
    >
      <LoaderCircle className={cn('animate-spin', spinnerSizes[size])} aria-hidden />
    </span>
  );
}

export type { SpinnerSize };
export { Spinner };
