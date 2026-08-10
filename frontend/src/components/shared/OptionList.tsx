import type { ReactNode } from 'react';
import { Spinner } from '@/components/ui/spinner';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { usePendingVisibility } from '@/hooks/usePendingVisibility';
import { cn } from '@/lib/utils';
import { toggledItemHighlight, transientItemHighlight } from './interactionStates';

interface OptionListProps {
  children: ReactNode;
  label: string;
  onValueChange: (value: string) => void;
  value: string;
}

interface OptionListItemProps {
  children: ReactNode;
  icon: ReactNode;
  pending?: boolean;
  value: string;
}

interface OptionItemContentProps {
  children: ReactNode;
  icon: ReactNode;
  pending?: boolean;
}

export function OptionList({ children, label, onValueChange, value }: OptionListProps) {
  return (
    <ToggleGroup
      aria-label={label}
      className="w-full"
      orientation="vertical"
      size="sm"
      value={[value]}
      onValueChange={(values) => {
        const nextValue = values[0];
        if (nextValue) onValueChange(nextValue);
      }}
    >
      {children}
    </ToggleGroup>
  );
}

export function OptionItemContent({ children, icon, pending = false }: OptionItemContentProps) {
  const showPending = usePendingVisibility(pending);

  return (
    <>
      <span
        data-slot="option-item-icon"
        className="flex size-axis-icon-control shrink-0 items-center justify-center text-axis-metadata font-axis-label leading-none"
        aria-hidden
      >
        {showPending ? <Spinner className="size-3.5" /> : icon}
      </span>
      <span data-slot="option-item-label" className="min-w-0 flex-1 truncate text-left">
        {children}
      </span>
    </>
  );
}

export function OptionListItem({ children, icon, pending = false, value }: OptionListItemProps) {
  return (
    <ToggleGroupItem
      className={cn(
        'min-h-axis-touch-target w-full justify-start sm:min-h-axis-compact-control',
        transientItemHighlight,
        toggledItemHighlight,
      )}
      value={value}
    >
      <OptionItemContent icon={icon} pending={pending}>
        {children}
      </OptionItemContent>
    </ToggleGroupItem>
  );
}
