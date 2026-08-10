import type { ReactNode } from 'react';
import { PendingIndicator } from '@/components/shared/PendingIndicator';
import { type PendingFeedbackKind, usePendingVisibility } from '@/hooks/usePendingVisibility';
import { cn } from '@/lib/utils';

interface AsyncContentProps {
  children: ReactNode;
  className?: string;
  error?: boolean;
  id?: string;
  kind?: PendingFeedbackKind;
  pending: boolean;
  pendingLabel: ReactNode;
}

export function AsyncContent({
  children,
  className,
  error = false,
  id,
  kind = 'feedback',
  pending,
  pendingLabel,
}: AsyncContentProps) {
  const showPending = usePendingVisibility(pending, kind);
  const blocked = !error && (pending || showPending);

  return (
    <div
      id={id}
      data-slot="async-content"
      aria-busy={blocked || undefined}
      aria-live="polite"
      className={cn('min-h-0 min-w-0', className)}
    >
      {blocked ? (
        <div data-slot="async-content-pending" className="min-h-5">
          {showPending ? <PendingIndicator>{pendingLabel}</PendingIndicator> : null}
        </div>
      ) : (
        children
      )}
    </div>
  );
}
