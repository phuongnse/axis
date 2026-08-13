import type { ComponentProps, ReactNode } from 'react';

import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';

type DataTableRecordActionProps = Pick<
  ComponentProps<typeof Button>,
  'aria-describedby' | 'aria-label' | 'disabled' | 'onClick' | 'onFocus' | 'onMouseEnter'
> & {
  children: ReactNode;
};

function DataTableRecordAction({ children, ...props }: DataTableRecordActionProps) {
  return (
    <Button
      {...props}
      type="button"
      variant="link"
      data-slot="data-table-record-action"
      className={cn(
        '-ml-px max-w-full justify-start overflow-hidden px-0 text-left',
        axisStyles.density.minHeight.touchTarget,
        axisStyles.density.minWidth.touchTarget,
        axisStyles.density.minHeight.compactControlAtSmall,
        axisStyles.density.minWidth.compactControlAtSmall,
      )}
    >
      <span data-slot="data-table-record-action-label" className="min-w-0 truncate">
        {children}
      </span>
    </Button>
  );
}

export { DataTableRecordAction, type DataTableRecordActionProps };
