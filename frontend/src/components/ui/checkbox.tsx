import { forwardRef, type InputHTMLAttributes } from 'react';

import { cn } from '@/lib/utils';

const Checkbox = forwardRef<HTMLInputElement, Omit<InputHTMLAttributes<HTMLInputElement>, 'type'>>(
  function Checkbox({ className, ...props }, ref) {
    return (
      <input
        ref={ref}
        type="checkbox"
        data-slot="checkbox"
        className={cn(
          'size-4 rounded border border-input bg-background accent-primary transition-colors',
          'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/30',
          'disabled:cursor-not-allowed disabled:opacity-50',
          'aria-invalid:border-destructive aria-invalid:ring-destructive/20',
          className,
        )}
        {...props}
      />
    );
  },
);

export { Checkbox };
