import type { LabelHTMLAttributes } from 'react';

import { cn } from '@/lib/utils';

function Label({ className, ...props }: LabelHTMLAttributes<HTMLLabelElement>) {
  return (
    // biome-ignore lint/a11y/noLabelWithoutControl: htmlFor is supplied by form fields
    <label
      className={cn('text-[11px] font-medium leading-none text-muted-foreground', className)}
      {...props}
    />
  );
}

export { Label };
