import type { Table } from '@tanstack/react-table';
import { Filter, Group, RotateCcw, Search, Settings2 } from 'lucide-react';
import type { ReactNode } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Input } from '@/components/ui/input';
import { Popover, PopoverContent, PopoverTitle, PopoverTrigger } from '@/components/ui/popover';
import { DataTableFilterBuilder, type DataTableFilterField } from './DataTableFilterBuilder';
import { countFilterConditions, createEmptyFilterExpression } from './filtering';
import type { DataTableFilterGroup, DataTableMessages } from './types';

interface DataTableToolbarProps<TData> {
  table: Table<TData>;
  messages: DataTableMessages;
  globalSearch: boolean;
  columnControls: boolean;
  grouping: boolean;
  filterExpression: DataTableFilterGroup;
  onFilterExpressionChange: (expression: DataTableFilterGroup) => void;
  actions?: ReactNode;
}

export function DataTableToolbar<TData>({
  table,
  messages,
  globalSearch,
  columnControls,
  grouping,
  filterExpression,
  onFilterExpressionChange,
  actions,
}: DataTableToolbarProps<TData>) {
  const filterFields = table
    .getVisibleLeafColumns()
    .flatMap<DataTableFilterField<TData>>((column) =>
      column.getCanFilter() && column.columnDef.meta?.filter
        ? [
            {
              id: column.id,
              label: column.columnDef.meta.label,
              definition: column.columnDef.meta.filter,
            },
          ]
        : [],
    );
  const activeFilterCount = countFilterConditions(filterExpression);
  const groupableColumns = table
    .getAllLeafColumns()
    .filter((column) => column.getCanGroup() && column.columnDef.meta?.label);
  const hideableColumns = table
    .getAllLeafColumns()
    .filter((column) => column.getCanHide() && column.columnDef.meta?.label);
  const hasQuery = Boolean(table.getState().globalFilter) || activeFilterCount > 0;

  function clearFilters() {
    table.resetGlobalFilter();
    onFilterExpressionChange(createEmptyFilterExpression());
  }

  return (
    <div
      data-slot="data-table-toolbar"
      className="flex min-w-0 shrink-0 flex-wrap items-center gap-2 border-b border-border bg-card px-3 py-2.5"
    >
      {globalSearch ? (
        <div className="relative min-w-48 flex-1 sm:max-w-sm">
          <Search
            className="pointer-events-none absolute top-1/2 left-2.5 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            value={String(table.getState().globalFilter ?? '')}
            onChange={(event) => table.setGlobalFilter(event.target.value)}
            placeholder={messages.searchPlaceholder}
            aria-label={messages.searchLabel}
            className="pl-8"
          />
        </div>
      ) : null}

      {filterFields.length > 0 ? (
        <Popover>
          <PopoverTrigger
            render={
              <Button type="button" variant="outline" size="sm" aria-label={messages.filters} />
            }
          >
            <Filter aria-hidden />
            {messages.filters}
            {activeFilterCount > 0 ? (
              <Badge variant="secondary" aria-hidden>
                {activeFilterCount}
              </Badge>
            ) : null}
          </PopoverTrigger>
          <PopoverContent
            align="start"
            className="max-h-[min(36rem,var(--available-height))] w-[min(52rem,calc(100vw-2rem))] overflow-y-auto"
          >
            <div className="mb-3 flex items-center justify-between gap-3">
              <PopoverTitle>{messages.filters}</PopoverTitle>
              {hasQuery ? (
                <Button type="button" variant="ghost" size="xs" onClick={clearFilters}>
                  <RotateCcw aria-hidden />
                  {messages.clearFilters}
                </Button>
              ) : null}
            </div>
            <DataTableFilterBuilder
              fields={filterFields}
              expression={filterExpression}
              messages={messages}
              onChange={onFilterExpressionChange}
            />
          </PopoverContent>
        </Popover>
      ) : null}

      {hasQuery ? (
        <Button type="button" variant="ghost" size="sm" onClick={clearFilters}>
          <RotateCcw aria-hidden />
          <span className="hidden sm:inline">{messages.clearFilters}</span>
        </Button>
      ) : null}

      {grouping && groupableColumns.length > 0 ? (
        <DropdownMenu>
          <DropdownMenuTrigger render={<Button type="button" variant="outline" size="sm" />}>
            <Group aria-hidden />
            {messages.grouping}
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuGroup>
              <DropdownMenuLabel>{messages.grouping}</DropdownMenuLabel>
              <DropdownMenuSeparator />
              {groupableColumns.map((column) => (
                <DropdownMenuCheckboxItem
                  key={column.id}
                  checked={column.getIsGrouped()}
                  onCheckedChange={(checked) => {
                    if (Boolean(checked) !== column.getIsGrouped()) column.toggleGrouping();
                  }}
                >
                  {column.columnDef.meta?.label}
                </DropdownMenuCheckboxItem>
              ))}
            </DropdownMenuGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      ) : null}

      {columnControls && hideableColumns.length > 0 ? (
        <DropdownMenu>
          <DropdownMenuTrigger render={<Button type="button" variant="outline" size="sm" />}>
            <Settings2 aria-hidden />
            {messages.columns}
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuGroup>
              <DropdownMenuLabel>{messages.columns}</DropdownMenuLabel>
              <DropdownMenuSeparator />
              {hideableColumns.map((column) => (
                <DropdownMenuCheckboxItem
                  key={column.id}
                  checked={column.getIsVisible()}
                  onCheckedChange={(checked) => column.toggleVisibility(Boolean(checked))}
                >
                  {column.columnDef.meta?.label}
                </DropdownMenuCheckboxItem>
              ))}
            </DropdownMenuGroup>
          </DropdownMenuContent>
        </DropdownMenu>
      ) : null}

      {actions ? (
        <div
          data-slot="data-table-toolbar-actions"
          className="ml-auto flex flex-wrap items-center gap-2"
        >
          {actions}
        </div>
      ) : null}
    </div>
  );
}
