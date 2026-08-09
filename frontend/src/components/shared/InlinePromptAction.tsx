import { Link } from '@tanstack/react-router';
import type { ComponentProps, ReactNode } from 'react';

import { Button, buttonVariants } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';
import { usePendingVisibility } from '@/hooks/usePendingVisibility';
import { cn } from '@/lib/utils';

const inlineActionClassName = 'h-auto border-0 p-0 text-xs';
const feedbackToneStyles = {
  success: 'text-success',
  warning: 'text-warning',
  destructive: 'text-destructive',
} as const;

interface InlinePromptActionProps {
  children: ReactNode;
  prompt: ReactNode;
}

type InlinePromptActionButtonProps = Omit<
  ComponentProps<typeof Button>,
  'className' | 'size' | 'variant'
> & {
  pending?: boolean;
  pendingLabel?: string;
};

type InlinePromptActionLinkProps = Omit<ComponentProps<typeof Link>, 'className'>;

interface InlinePromptActionFeedbackProps {
  children: ReactNode;
  tone: keyof typeof feedbackToneStyles;
}

function InlinePromptAction({ children, prompt }: InlinePromptActionProps) {
  return (
    <span className="inline-flex max-w-full flex-wrap items-baseline gap-x-1 gap-y-1 text-xs text-muted-foreground">
      <span>{prompt}</span>
      {children}
    </span>
  );
}

function InlinePromptActionButton({
  children,
  disabled,
  pending = false,
  pendingLabel,
  ...props
}: InlinePromptActionButtonProps) {
  const showPending = usePendingVisibility(pending);
  const busy = pending || showPending;

  return (
    <Button
      variant="link"
      className={inlineActionClassName}
      aria-busy={busy || undefined}
      disabled={disabled || busy}
      {...props}
    >
      {pendingLabel !== undefined ? (
        <span className="flex size-3.5 shrink-0 items-center justify-center" aria-hidden>
          {showPending ? <Spinner className="size-3" /> : null}
        </span>
      ) : null}
      {children}
      {busy && pendingLabel ? (
        <span className="sr-only" role="status">
          {pendingLabel}
        </span>
      ) : null}
    </Button>
  );
}

function InlinePromptActionLink(props: InlinePromptActionLinkProps) {
  return (
    <Link className={cn(buttonVariants({ variant: 'link' }), inlineActionClassName)} {...props} />
  );
}

function InlinePromptActionFeedback({ children, tone }: InlinePromptActionFeedbackProps) {
  return (
    <p className={cn('text-xs', feedbackToneStyles[tone])} role="status" aria-live="polite">
      {children}
    </p>
  );
}

export {
  InlinePromptAction,
  InlinePromptActionButton,
  InlinePromptActionFeedback,
  InlinePromptActionLink,
};
