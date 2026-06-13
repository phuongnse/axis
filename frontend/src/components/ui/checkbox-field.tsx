import type { InputHTMLAttributes, ReactNode } from 'react';

import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { cn } from '@/lib/utils';

interface CheckboxFieldProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'children' | 'id' | 'type'> {
  id: string;
  children: ReactNode;
  error?: string;
}

function CheckboxField({ id, children, error, className, ...props }: CheckboxFieldProps) {
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [props['aria-describedby'], errorId].filter(Boolean).join(' ') || undefined;

  return (
    <div className="space-y-1.5">
      <div className="flex items-center gap-2">
        <Checkbox
          {...props}
          id={id}
          className={cn('shrink-0', className)}
          aria-describedby={describedBy}
          aria-invalid={error ? true : props['aria-invalid']}
        />
        <Label htmlFor={id} className="font-normal leading-4 text-muted-foreground">
          {children}
        </Label>
      </div>
      {error ? (
        <p id={errorId} className="text-xs text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export { CheckboxField };
