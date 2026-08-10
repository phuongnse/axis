import type { ComponentProps, ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';
import { usePendingVisibility } from '@/hooks/usePendingVisibility';

type AsyncButtonProps = Omit<ComponentProps<typeof Button>, 'children'> & {
  children: string;
  icon: ReactNode;
  pending: boolean;
  pendingLabel: string;
};

export function AsyncButton({
  'aria-label': ariaLabel,
  children,
  disabled,
  icon,
  pending,
  pendingLabel,
  ...props
}: AsyncButtonProps) {
  const showPending = usePendingVisibility(pending);
  const busy = pending || showPending;

  return (
    <Button
      {...props}
      aria-label={ariaLabel ?? children}
      aria-busy={busy || undefined}
      disabled={disabled || busy}
    >
      <span
        data-slot="async-button-icon"
        className="flex size-axis-icon-control shrink-0 items-center justify-center"
        aria-hidden
      >
        {showPending ? <Spinner /> : icon}
      </span>
      <span>{children}</span>
      {busy ? (
        <span className="sr-only" role="status">
          {pendingLabel}
        </span>
      ) : null}
    </Button>
  );
}
