import type { ReactNode } from 'react';
import { Spinner } from '@/components/ui/spinner';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
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
  busy?: boolean;
  children: ReactNode;
  icon: ReactNode;
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

export function OptionItemContent({ busy = false, children, icon }: OptionItemContentProps) {
  return (
    <>
      <span
        data-slot="option-item-icon"
        className="flex size-axis-icon-control shrink-0 items-center justify-center text-axis-metadata font-axis-label leading-none"
        aria-hidden
      >
        {busy ? <Spinner className="size-3.5" /> : icon}
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
      aria-busy={pending || undefined}
      className={cn(
        'min-h-axis-touch-target w-full justify-start px-axis-inline sm:min-h-axis-compact-control',
        transientItemHighlight,
        toggledItemHighlight,
      )}
      disabled={pending}
      value={value}
    >
      <OptionItemContent busy={pending} icon={icon}>
        {children}
      </OptionItemContent>
    </ToggleGroupItem>
  );
}
