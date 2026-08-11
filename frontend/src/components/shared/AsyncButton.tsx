import type { ComponentProps, ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

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
  return (
    <Button
      {...props}
      aria-label={ariaLabel ?? children}
      aria-busy={pending || undefined}
      disabled={disabled || pending}
    >
      <span
        data-slot="async-button-icon"
        className={cn('flex shrink-0 items-center justify-center', axisStyles.icon.size.control)}
        aria-hidden
      >
        {pending ? <Spinner /> : icon}
      </span>
      <span>{children}</span>
      {pending ? (
        <span className="sr-only" role="status">
          {pendingLabel}
        </span>
      ) : null}
    </Button>
  );
}
