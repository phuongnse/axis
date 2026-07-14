import type { ReactNode } from 'react';
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
  value: string;
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

export function OptionListItem({ children, value }: OptionListItemProps) {
  return (
    <ToggleGroupItem
      className={cn('w-full justify-start', transientItemHighlight, toggledItemHighlight)}
      value={value}
    >
      {children}
    </ToggleGroupItem>
  );
}
