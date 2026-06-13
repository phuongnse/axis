import { CircleHelp } from 'lucide-react';
import type { ReactNode } from 'react';

import { Label } from '@/components/ui/label';

interface FormFieldProps {
  id: string;
  label: string;
  helpText?: string;
  descriptionIds?: string[];
  error?: string;
  children: (props: { describedBy?: string }) => ReactNode;
}

function FormField({ id, label, helpText, descriptionIds = [], error, children }: FormFieldProps) {
  const helpId = helpText ? `${id}-help` : undefined;
  const errorId = error ? `${id}-error` : undefined;
  const describedBy = [helpId, ...descriptionIds, errorId].filter(Boolean).join(' ') || undefined;

  return (
    <div className="space-y-2">
      <div className="space-y-1.5">
        <Label htmlFor={id}>{label}</Label>
        {helpText ? (
          <p
            id={helpId}
            className="flex items-start gap-1.5 text-[11px] leading-4 text-muted-foreground"
          >
            <CircleHelp className="mt-0.5 size-3.5 shrink-0 text-primary/80" aria-hidden />
            <span>{helpText}</span>
          </p>
        ) : null}
      </div>
      {children({ describedBy })}
      {error ? (
        <p id={errorId} className="text-xs leading-5 text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}

export { FormField };
