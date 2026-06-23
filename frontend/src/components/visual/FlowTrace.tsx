import type { LucideIcon } from 'lucide-react';
import { AlertCircle, CheckCircle2, Circle, Loader2 } from 'lucide-react';

import { cn } from '@/lib/utils';

export type FlowTraceState = 'complete' | 'active' | 'pending' | 'failed';
type FlowTraceSize = 'sm' | 'md' | 'lg';
type FlowTraceIconMode = 'state' | 'provided';

export interface FlowTraceStep {
  id: string;
  label: string;
  meta?: string;
  state?: FlowTraceState;
  icon?: LucideIcon;
}

interface FlowTraceProps {
  steps: FlowTraceStep[];
  className?: string;
  dense?: boolean;
  size?: FlowTraceSize;
  iconMode?: FlowTraceIconMode;
  markerClassName?: string;
  connectorClassName?: string;
  titleClassName?: string;
  metaClassName?: string;
  stateClassName?: Partial<Record<FlowTraceState, string>>;
}

const stateClass: Record<FlowTraceState, string> = {
  complete: 'border-primary bg-primary text-primary-foreground',
  active: 'border-accent bg-accent text-accent-foreground',
  pending: 'border-border bg-card text-muted-foreground',
  failed: 'border-destructive bg-destructive text-destructive-foreground',
};

const sizeClass: Record<
  FlowTraceSize,
  { row: string; marker: string; icon: string; gap: string; pendingPadding: string }
> = {
  sm: {
    row: 'grid-cols-[28px_1fr]',
    marker: 'size-7',
    icon: 'size-3.5',
    gap: 'gap-3',
    pendingPadding: 'pb-3.5',
  },
  md: {
    row: 'grid-cols-[34px_1fr]',
    marker: 'size-8',
    icon: 'size-4',
    gap: 'gap-4',
    pendingPadding: 'pb-7',
  },
  lg: {
    row: 'grid-cols-[40px_1fr]',
    marker: 'size-9',
    icon: 'size-4',
    gap: 'gap-4',
    pendingPadding: 'pb-8',
  },
};

function FlowTraceStateIcon({
  state,
  icon: Icon,
  iconClassName,
  iconMode,
}: {
  state: FlowTraceState;
  icon?: LucideIcon;
  iconClassName: string;
  iconMode: FlowTraceIconMode;
}) {
  if (iconMode === 'provided' && Icon) return <Icon className={iconClassName} aria-hidden />;
  if (state === 'complete') return <CheckCircle2 className={iconClassName} aria-hidden />;
  if (state === 'active')
    return <Loader2 className={cn(iconClassName, 'animate-spin')} aria-hidden />;
  if (state === 'failed') return <AlertCircle className={iconClassName} aria-hidden />;
  if (Icon) return <Icon className={iconClassName} aria-hidden />;
  return <Circle className={iconClassName} aria-hidden />;
}

export function FlowTrace({
  steps,
  className,
  dense = false,
  size = 'sm',
  iconMode = 'state',
  markerClassName,
  connectorClassName,
  titleClassName,
  metaClassName,
  stateClassName,
}: FlowTraceProps) {
  const classes = sizeClass[size];

  return (
    <ol className={cn('space-y-0', className)}>
      {steps.map((step, index) => {
        const state = step.state ?? 'pending';
        const isLast = index === steps.length - 1;
        return (
          <li key={step.id} className={cn('grid', classes.row, classes.gap)}>
            <div className="flex flex-col items-center">
              <span
                className={cn(
                  'inline-flex items-center justify-center rounded-md border shadow-surface',
                  classes.marker,
                  markerClassName,
                  stateClass[state],
                  stateClassName?.[state],
                )}
              >
                <FlowTraceStateIcon
                  state={state}
                  icon={step.icon}
                  iconClassName={classes.icon}
                  iconMode={iconMode}
                />
              </span>
              {!isLast ? (
                <span
                  className={cn(
                    'mt-2 mb-2 min-h-7 w-px flex-1',
                    state === 'pending' ? 'bg-border' : 'bg-primary/45',
                    connectorClassName,
                  )}
                  aria-hidden
                />
              ) : null}
            </div>
            <div className={cn(dense ? 'pt-0.5' : 'pt-0', !isLast && classes.pendingPadding)}>
              <p
                className={cn(
                  'text-sm font-medium leading-5',
                  state === 'pending' ? 'text-muted-foreground' : 'text-foreground',
                  titleClassName,
                )}
              >
                {step.label}
              </p>
              {step.meta ? (
                <p className={cn('text-xs text-muted-foreground', metaClassName)}>{step.meta}</p>
              ) : null}
            </div>
          </li>
        );
      })}
    </ol>
  );
}
