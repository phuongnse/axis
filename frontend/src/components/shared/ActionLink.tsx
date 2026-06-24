import { Link } from '@tanstack/react-router';
import type { LucideIcon } from 'lucide-react';
import type { ComponentProps, ReactNode } from 'react';

import { cn } from '@/lib/utils';

type ActionLinkSurface = 'default' | 'inverted' | 'adaptive';
type ActionLinkVariant = 'primary' | 'secondary';

type RouterLinkProps = ComponentProps<typeof Link>;

interface ActionLinkProps extends Omit<RouterLinkProps, 'children' | 'className'> {
  children: ReactNode;
  icon: LucideIcon;
  surface?: ActionLinkSurface;
  variant?: ActionLinkVariant;
  className?: string;
}

const actionLinkClass: Record<ActionLinkSurface, Record<ActionLinkVariant, string>> = {
  default: {
    primary:
      'border-action-accent-border bg-accent text-accent-foreground shadow-accent-control hover:bg-accent/90',
    secondary:
      'border-border bg-background text-foreground hover:bg-muted hover:text-foreground dark:border-input dark:bg-input/30 dark:hover:bg-input/50',
  },
  inverted: {
    primary:
      'border-inverse-foreground bg-inverse-foreground text-inverse hover:bg-inverse-foreground/90',
    secondary:
      'border-inverse-border bg-inverse-muted text-inverse-foreground hover:bg-inverse-muted',
  },
  adaptive: {
    primary:
      'border-action-accent-border bg-accent text-accent-foreground shadow-accent-control hover:bg-accent/90 dark:border-inverse-foreground dark:bg-inverse-foreground dark:text-inverse dark:hover:bg-inverse-foreground/90',
    secondary:
      'border-border bg-background text-foreground hover:bg-muted hover:text-foreground dark:border-inverse-border dark:bg-inverse-muted dark:text-inverse-foreground dark:hover:bg-inverse-muted',
  },
};

export function ActionLink({
  children,
  icon: Icon,
  surface = 'default',
  variant = 'primary',
  className,
  ...props
}: ActionLinkProps) {
  return (
    <Link
      className={cn(
        'inline-flex h-9 items-center justify-center gap-2 rounded-md border px-4 text-sm font-medium transition-colors',
        actionLinkClass[surface][variant],
        className,
      )}
      {...props}
    >
      <Icon className="size-4" aria-hidden />
      {children}
    </Link>
  );
}
