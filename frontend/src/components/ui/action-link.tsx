import { Link } from '@tanstack/react-router';
import type { LucideIcon } from 'lucide-react';
import type { ComponentProps, ReactNode } from 'react';

import { cn } from '@/lib/utils';

type ActionLinkSurface = 'default' | 'inverted';
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
      'border-[hsl(32_62%_40%)] bg-accent text-accent-foreground shadow-[0_1px_0_hsl(32_62%_32%)] hover:bg-accent/90',
    secondary:
      'border-border bg-background text-foreground hover:bg-muted hover:text-foreground dark:border-input dark:bg-input/30 dark:hover:bg-input/50',
  },
  inverted: {
    primary: 'border-white bg-white text-[hsl(174_25%_12%)] hover:bg-white/90',
    secondary: 'border-white/20 bg-white/[0.06] text-white hover:bg-white/10',
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
