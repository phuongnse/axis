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
      'border-[hsl(var(--action-accent-border))] bg-accent text-accent-foreground shadow-[0_1px_0_hsl(var(--action-accent-shadow))] hover:bg-accent/90',
    secondary:
      'border-border bg-background text-foreground hover:bg-muted hover:text-foreground dark:border-input dark:bg-input/30 dark:hover:bg-input/50',
  },
  inverted: {
    primary: 'border-white bg-white text-[hsl(var(--action-inverse-foreground))] hover:bg-white/90',
    secondary: 'border-white/20 bg-white/[0.06] text-white hover:bg-white/10',
  },
  adaptive: {
    primary:
      'border-[hsl(var(--action-accent-border))] bg-accent text-accent-foreground shadow-[0_1px_0_hsl(var(--action-accent-shadow))] hover:bg-accent/90 dark:border-white dark:bg-white dark:text-[hsl(var(--action-inverse-foreground))] dark:hover:bg-white/90',
    secondary:
      'border-border bg-background text-foreground hover:bg-muted hover:text-foreground dark:border-white/20 dark:bg-white/[0.06] dark:text-white dark:hover:bg-white/10',
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
