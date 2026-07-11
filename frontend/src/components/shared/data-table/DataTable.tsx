import {
  type Column,
  type ColumnDef,
  flexRender,
  functionalUpdate,
  getCoreRowModel,
  getExpandedRowModel,
  getFilteredRowModel,
  getGroupedRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  type PaginationState,
  type Row,
  type Updater,
  useReactTable,
} from '@tanstack/react-table';
import { useVirtualizer } from '@tanstack/react-virtual';
import {
  ArrowDown,
  ArrowUp,
  ArrowUpDown,
  ChevronDown,
  ChevronFirst,
  ChevronLast,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  EyeOff,
  ListX,
  LoaderCircle,
  Pin,
  PinOff,
  RefreshCw,
  TriangleAlert,
} from 'lucide-react';
import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Empty,
  EmptyContent,
  EmptyDescription,
  EmptyHeader,
  EmptyMedia,
  EmptyTitle,
} from '@/components/ui/empty';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Skeleton } from '@/components/ui/skeleton';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { cn } from '@/lib/utils';
import { DataTableToolbar } from './DataTableToolbar';
import {
  countFilterConditions,
  createEmptyFilterExpression,
  filterData,
  normalizeSearchValue,
  pruneFilterExpression,
} from './filtering';
import type { DataTableDefinition, DataTableMessages, DataTableQueryState } from './types';

const selectionColumnId = '__selection';
const defaultPageSizeOptions = [10, 20, 50, 100] as const;
const loadingRowIds = ['one', 'two', 'three', 'four', 'five', 'six'] as const;

export function DataTable<TData>({ definition }: { definition: DataTableDefinition<TData> }) {
  const { source, messages } = definition;
  const controlledQuery = definition.queryState;
  const [internalQuery, setInternalQuery] = useState<DataTableQueryState>(() => ({
    globalFilter: definition.initialState?.globalFilter ?? '',
    filterExpression: pruneFilterExpression(
      definition.initialState?.filterExpression ?? createEmptyFilterExpression(),
      new Set(
        definition.columns.flatMap((column) => {
          const id = column.id ?? ('accessorKey' in column ? String(column.accessorKey) : '');
          return id && definition.initialState?.columnVisibility?.[id] !== false ? [id] : [];
        }),
      ),
    ),
    sorting: definition.initialState?.sorting ?? [],
    grouping: definition.initialState?.grouping ?? [],
  }));
  const query = controlledQuery ?? internalQuery;
  const [columnVisibility, setColumnVisibility] = useState(
    definition.initialState?.columnVisibility ?? {},
  );
  const [columnOrder, setColumnOrder] = useState(definition.initialState?.columnOrder ?? []);
  const [columnPinning, setColumnPinning] = useState(definition.initialState?.columnPinning ?? {});
  const [expanded, setExpanded] = useState(definition.initialState?.expanded ?? {});
  const [rowSelection, setRowSelection] = useState(definition.initialState?.rowSelection ?? {});
  const [clientPagination, setClientPagination] = useState<PaginationState>(() => ({
    pageIndex: 0,
    pageSize:
      source.mode === 'client' && source.pagination ? (source.pagination.pageSize ?? 20) : 20,
  }));
  const scrollRef = useRef<HTMLDivElement>(null);

  const updateQuery = useCallback(
    (next: DataTableQueryState) => {
      if (!controlledQuery) setInternalQuery(next);
      definition.onQueryStateChange?.(next);
      if (source.mode === 'page' && source.pagination.pageIndex !== 0) {
        source.onPaginationChange({ ...source.pagination, pageIndex: 0 });
      }
      if (source.mode === 'client' && source.pagination) {
        setClientPagination((current) => ({ ...current, pageIndex: 0 }));
      }
    },
    [controlledQuery, definition.onQueryStateChange, source],
  );

  const updateQueryPart = useCallback(
    <K extends keyof DataTableQueryState>(key: K, updater: Updater<DataTableQueryState[K]>) => {
      updateQuery({ ...query, [key]: functionalUpdate(updater, query[key]) });
    },
    [query, updateQuery],
  );

  const columns = useMemo<ColumnDef<TData, unknown>[]>(() => {
    const configured = [...definition.columns];
    if (!definition.enableRowSelection) return configured;
    return [
      {
        id: selectionColumnId,
        size: 40,
        minSize: 40,
        maxSize: 40,
        enableHiding: false,
        enableSorting: false,
        enableColumnFilter: false,
        enableGlobalFilter: false,
        enableGrouping: false,
        enablePinning: true,
        meta: { label: messages.selectRow, searchable: false },
        header: ({ table }) => (
          <Checkbox
            aria-label={messages.selectAllRows}
            checked={table.getIsAllPageRowsSelected()}
            indeterminate={table.getIsSomePageRowsSelected()}
            onCheckedChange={(checked) => table.toggleAllPageRowsSelected(Boolean(checked))}
          />
        ),
        cell: ({ row }) => (
          <Checkbox
            aria-label={messages.selectRow}
            checked={row.getIsSelected()}
            disabled={!row.getCanSelect()}
            onCheckedChange={(checked) => row.toggleSelected(Boolean(checked))}
          />
        ),
      },
      ...configured,
    ];
  }, [definition.columns, definition.enableRowSelection, messages]);
  const data = useMemo(
    () =>
      source.mode === 'client'
        ? filterData(source.data, query.filterExpression, definition.columns)
        : [...source.data],
    [definition.columns, query.filterExpression, source],
  );

  const pagination = source.mode === 'page' ? source.pagination : clientPagination;
  const clientMode = source.mode === 'client';
  const clientNumberedPagination = clientMode && source.pagination !== false;

  const table = useReactTable({
    data,
    columns,
    getRowId: definition.getRowId,
    getSubRows: definition.getSubRows,
    getRowCanExpand: definition.renderDetail ? () => true : undefined,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: clientMode ? getFilteredRowModel() : undefined,
    getSortedRowModel: clientMode ? getSortedRowModel() : undefined,
    getGroupedRowModel: clientMode ? getGroupedRowModel() : undefined,
    getExpandedRowModel: getExpandedRowModel(),
    getPaginationRowModel: clientNumberedPagination ? getPaginationRowModel() : undefined,
    manualFiltering: !clientMode,
    manualSorting: !clientMode,
    manualGrouping: !clientMode,
    manualPagination: source.mode === 'page',
    rowCount: source.mode === 'page' ? source.rowCount : undefined,
    enableMultiSort: definition.enableMultiSort ?? true,
    enableGrouping: definition.grouping ?? false,
    enableColumnResizing: definition.enableColumnResizing ?? false,
    columnResizeMode: 'onChange',
    enableRowSelection: definition.enableRowSelection,
    getColumnCanGlobalFilter: (column) =>
      column.columnDef.meta?.searchable !== false && Boolean(column.columnDef.meta?.label),
    globalFilterFn: (row, columnId, filterValue) => {
      if (columnVisibility[columnId] === false) return false;
      const cell = row.getAllCells().find((candidate) => candidate.column.id === columnId);
      const value = cell?.column.columnDef.meta?.searchValue
        ? cell.column.columnDef.meta.searchValue(row.original)
        : row.getValue(columnId);
      return normalizeSearchValue(value).includes(normalizeSearchValue(filterValue));
    },
    state: {
      globalFilter: query.globalFilter,
      sorting: query.sorting,
      grouping: query.grouping,
      columnVisibility,
      columnOrder,
      columnPinning,
      expanded,
      rowSelection,
      pagination,
    },
    onGlobalFilterChange: (updater) => updateQueryPart('globalFilter', updater),
    onSortingChange: (updater) => updateQueryPart('sorting', updater),
    onGroupingChange: (updater) => updateQueryPart('grouping', updater),
    onExpandedChange: setExpanded,
    onRowSelectionChange: setRowSelection,
    onColumnOrderChange: setColumnOrder,
    onColumnPinningChange: setColumnPinning,
    onColumnVisibilityChange: (updater) => {
      const next = functionalUpdate(updater, columnVisibility);
      setColumnVisibility(next);
      const visibleFilterFields = new Set(
        definition.columns.flatMap((column) => {
          const id = column.id ?? ('accessorKey' in column ? String(column.accessorKey) : '');
          return id && next[id] !== false && column.meta?.filter ? [id] : [];
        }),
      );
      const filterExpression = pruneFilterExpression(query.filterExpression, visibleFilterFields);
      if (filterExpression !== query.filterExpression) {
        updateQuery({ ...query, filterExpression });
      }
    },
    onPaginationChange: (updater) => {
      const next = functionalUpdate(updater, pagination);
      if (source.mode === 'page') source.onPaginationChange(next);
      else setClientPagination(next);
    },
  });

  const rows = table.getRowModel().rows;
  const virtualized = source.mode === 'infinite' && Boolean(source.virtualize);
  const rowVirtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => (source.mode === 'infinite' ? (source.estimateRowHeight ?? 52) : 52),
    overscan: 8,
    enabled: virtualized,
  });

  const fetchMoreIfNeeded = useCallback(() => {
    if (source.mode !== 'infinite' || !source.hasNextPage || source.isFetchingNextPage) return;
    const viewport = scrollRef.current;
    if (!viewport) return;
    const threshold = source.fetchThreshold ?? 400;
    if (viewport.scrollHeight - viewport.scrollTop - viewport.clientHeight <= threshold) {
      void source.fetchNextPage();
    }
  }, [source]);

  useEffect(() => {
    if (source.mode === 'infinite') fetchMoreIfNeeded();
  }, [fetchMoreIfNeeded, source.mode]);

  const selectedRows = table.getSelectedRowModel().flatRows;
  const hasRows = rows.length > 0;
  const hasQuery = Boolean(query.globalFilter) || countFilterConditions(query.filterExpression) > 0;
  const visibleColumnCount = Math.max(table.getVisibleLeafColumns().length, 1);

  return (
    <section
      aria-label={definition.ariaLabel}
      aria-busy={definition.loading || undefined}
      data-slot="data-table"
      data-mode={source.mode}
      className="flex h-full min-h-0 min-w-0 flex-col overflow-hidden rounded-sm border border-border bg-card"
    >
      <DataTableToolbar
        table={table}
        messages={messages}
        globalSearch={definition.globalSearch ?? true}
        columnControls={definition.columnControls ?? true}
        grouping={definition.grouping ?? false}
        filterExpression={query.filterExpression}
        onFilterExpressionChange={(filterExpression) => updateQuery({ ...query, filterExpression })}
        actions={definition.renderToolbarActions?.({
          rows,
          selectedRows,
          queryState: query,
          clearSelection: () => table.resetRowSelection(),
        })}
      />

      {definition.renderBulkActions && selectedRows.length > 0 ? (
        <div
          data-slot="data-table-bulk-actions"
          className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-b border-border bg-muted/50 px-3 py-2"
        >
          <span className="text-sm text-muted-foreground">
            {messages.selectedStatus(selectedRows.length, table.getRowCount())}
          </span>
          {definition.renderBulkActions(selectedRows, () => table.resetRowSelection())}
        </div>
      ) : null}

      <Table
        containerRef={scrollRef}
        onContainerScroll={fetchMoreIfNeeded}
        containerClassName="min-h-0 flex-1 overscroll-contain"
        className={cn('table-fixed', virtualized && 'grid')}
        style={{ width: table.getTotalSize(), minWidth: '100%' }}
      >
        <TableHeader
          className={cn(
            'sticky top-0 z-20 bg-card shadow-[0_1px_0_var(--border)]',
            virtualized && 'grid',
          )}
        >
          {table.getHeaderGroups().map((headerGroup) => (
            <TableRow
              key={headerGroup.id}
              className={cn('hover:bg-transparent', virtualized && 'flex w-full')}
            >
              {headerGroup.headers.map((header) => (
                <TableHead
                  key={header.id}
                  colSpan={header.colSpan}
                  style={{
                    width: header.getSize(),
                    ...pinnedColumnStyle(header.column),
                  }}
                  className="relative bg-card"
                >
                  {header.isPlaceholder ? null : (
                    <DataTableColumnHeader column={header.column} messages={messages}>
                      {header.column.columnDef.meta?.label ??
                        flexRender(header.column.columnDef.header, header.getContext())}
                    </DataTableColumnHeader>
                  )}
                  {header.column.getCanResize() ? (
                    <Button
                      type="button"
                      variant="ghost"
                      size="inline"
                      aria-label={`${header.column.columnDef.meta?.label ?? header.column.id}: resize`}
                      data-slot="data-table-resizer"
                      data-resizing={header.column.getIsResizing()}
                      onDoubleClick={() => header.column.resetSize()}
                      onMouseDown={header.getResizeHandler()}
                      onTouchStart={header.getResizeHandler()}
                      className="absolute top-0 right-0 h-full w-1 cursor-col-resize touch-none rounded-none p-0 hover:bg-primary/30 data-[resizing=true]:bg-primary"
                    />
                  ) : null}
                </TableHead>
              ))}
            </TableRow>
          ))}
        </TableHeader>

        <TableBody
          className={cn(virtualized && 'relative grid')}
          style={virtualized ? { height: rowVirtualizer.getTotalSize() } : undefined}
        >
          {definition.loading ? (
            <LoadingRows
              columnIds={table.getVisibleLeafColumns().map((column) => column.id)}
              messages={messages}
            />
          ) : definition.error ? (
            <StateRow columnCount={visibleColumnCount}>
              <Empty role="alert">
                <EmptyHeader>
                  <EmptyMedia variant="icon">
                    <TriangleAlert aria-hidden />
                  </EmptyMedia>
                  <EmptyTitle>{messages.errorTitle}</EmptyTitle>
                  <EmptyDescription>{messages.errorDescription}</EmptyDescription>
                </EmptyHeader>
                {definition.onRetry ? (
                  <EmptyContent>
                    <Button type="button" variant="outline" onClick={definition.onRetry}>
                      <RefreshCw aria-hidden />
                      {messages.retry}
                    </Button>
                  </EmptyContent>
                ) : null}
              </Empty>
            </StateRow>
          ) : hasRows ? (
            virtualized ? (
              rowVirtualizer.getVirtualItems().map((virtualRow) => {
                const row = rows[virtualRow.index];
                return (
                  <DataRow
                    key={row.id}
                    row={row}
                    messages={messages}
                    renderDetail={definition.renderDetail}
                    virtual={{ start: virtualRow.start, measure: rowVirtualizer.measureElement }}
                  />
                );
              })
            ) : (
              rows.map((row) => (
                <DataRow
                  key={row.id}
                  row={row}
                  messages={messages}
                  renderDetail={definition.renderDetail}
                />
              ))
            )
          ) : (
            <StateRow columnCount={visibleColumnCount}>
              <Empty>
                <EmptyHeader>
                  <EmptyMedia variant="icon">
                    <ListX aria-hidden />
                  </EmptyMedia>
                  <EmptyTitle>
                    {hasQuery ? messages.noResultsTitle : messages.emptyTitle}
                  </EmptyTitle>
                  <EmptyDescription>
                    {hasQuery ? messages.noResultsDescription : messages.emptyDescription}
                  </EmptyDescription>
                </EmptyHeader>
              </Empty>
            </StateRow>
          )}
        </TableBody>
      </Table>

      <DataTableFooter table={table} source={source} messages={messages} />
    </section>
  );
}

function DataTableColumnHeader<TData>({
  column,
  messages,
  children,
}: {
  column: Column<TData, unknown>;
  messages: DataTableMessages;
  children: React.ReactNode;
}) {
  const sorted = column.getIsSorted();
  const configurable = column.getCanHide() || column.getCanPin();
  return (
    <div className="flex min-w-0 items-center gap-1">
      {column.getCanSort() ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="min-w-0 justify-start"
          aria-label={`${column.columnDef.meta?.label ?? column.id}: ${
            sorted === 'asc'
              ? messages.sortDescending
              : sorted === 'desc'
                ? messages.clearSorting
                : messages.sortAscending
          }`}
          onClick={() =>
            sorted === 'asc'
              ? column.toggleSorting(true)
              : sorted === 'desc'
                ? column.clearSorting()
                : column.toggleSorting(false)
          }
        >
          <span className="truncate">{children}</span>
          {sorted === 'asc' ? (
            <ArrowUp aria-hidden />
          ) : sorted === 'desc' ? (
            <ArrowDown aria-hidden />
          ) : (
            <ArrowUpDown aria-hidden />
          )}
        </Button>
      ) : (
        <span className="truncate">{children}</span>
      )}
      {configurable ? (
        <DropdownMenu>
          <DropdownMenuTrigger
            render={
              <Button
                type="button"
                variant="ghost"
                size="icon-xs"
                aria-label={`${column.columnDef.meta?.label ?? column.id}: ${messages.columns}`}
              />
            }
          >
            <ChevronDown aria-hidden />
          </DropdownMenuTrigger>
          <DropdownMenuContent align="start">
            {column.getCanSort() ? (
              <>
                <DropdownMenuItem onClick={() => column.toggleSorting(false)}>
                  <ArrowUp aria-hidden />
                  {messages.sortAscending}
                </DropdownMenuItem>
                <DropdownMenuItem onClick={() => column.toggleSorting(true)}>
                  <ArrowDown aria-hidden />
                  {messages.sortDescending}
                </DropdownMenuItem>
                <DropdownMenuItem onClick={() => column.clearSorting()}>
                  <ArrowUpDown aria-hidden />
                  {messages.clearSorting}
                </DropdownMenuItem>
                <DropdownMenuSeparator />
              </>
            ) : null}
            {column.getCanPin() ? (
              column.getIsPinned() ? (
                <DropdownMenuItem onClick={() => column.pin(false)}>
                  <PinOff aria-hidden />
                  {messages.unpin}
                </DropdownMenuItem>
              ) : (
                <>
                  <DropdownMenuItem onClick={() => column.pin('left')}>
                    <Pin aria-hidden />
                    {messages.pinLeft}
                  </DropdownMenuItem>
                  <DropdownMenuItem onClick={() => column.pin('right')}>
                    <Pin aria-hidden />
                    {messages.pinRight}
                  </DropdownMenuItem>
                </>
              )
            ) : null}
            {column.getCanHide() ? (
              <DropdownMenuItem onClick={() => column.toggleVisibility(false)}>
                <EyeOff aria-hidden />
                {messages.hideColumn}
              </DropdownMenuItem>
            ) : null}
          </DropdownMenuContent>
        </DropdownMenu>
      ) : null}
    </div>
  );
}

function DataRow<TData>({
  row,
  messages,
  renderDetail,
  virtual,
}: {
  row: Row<TData>;
  messages: DataTableMessages;
  renderDetail?: (row: Row<TData>) => React.ReactNode;
  virtual?: { start: number; measure: (element: Element | null) => void };
}) {
  const visibleCells = row.getVisibleCells();
  const detail =
    row.getIsExpanded() && renderDetail && !row.getIsGrouped() ? renderDetail(row) : null;
  return (
    <Fragment>
      <TableRow
        ref={virtual ? virtual.measure : undefined}
        data-index={virtual ? row.index : undefined}
        data-state={row.getIsSelected() ? 'selected' : undefined}
        className={cn(virtual && 'absolute flex w-full')}
        style={virtual ? { transform: `translateY(${virtual.start}px)` } : undefined}
      >
        {visibleCells.map((cell, index) => {
          const grouped = cell.getIsGrouped();
          const aggregated = cell.getIsAggregated();
          const isAggregateCell = cell.getIsPlaceholder();
          const canExpand =
            index === (visibleCells[0]?.column.id === selectionColumnId ? 1 : 0) &&
            row.getCanExpand();
          return (
            <TableCell
              key={cell.id}
              style={{
                width: cell.column.getSize(),
                ...pinnedColumnStyle(cell.column),
              }}
              className={cn('bg-card', virtual && 'flex shrink-0 items-center')}
            >
              <div
                className="flex min-w-0 items-center gap-1.5"
                style={canExpand && row.depth > 0 ? { paddingLeft: row.depth * 16 } : undefined}
              >
                {grouped || canExpand ? (
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-xs"
                    aria-label={row.getIsExpanded() ? messages.collapseRow : messages.expandRow}
                    onClick={row.getToggleExpandedHandler()}
                  >
                    {row.getIsExpanded() ? <ChevronUp aria-hidden /> : <ChevronRight aria-hidden />}
                  </Button>
                ) : null}
                {grouped ? (
                  <>
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    <span className="text-xs text-muted-foreground">({row.subRows.length})</span>
                  </>
                ) : aggregated ? (
                  flexRender(
                    cell.column.columnDef.aggregatedCell ?? cell.column.columnDef.cell,
                    cell.getContext(),
                  )
                ) : isAggregateCell ? null : (
                  flexRender(cell.column.columnDef.cell, cell.getContext())
                )}
              </div>
            </TableCell>
          );
        })}
      </TableRow>
      {detail ? (
        <TableRow>
          <TableCell colSpan={visibleCells.length} className="bg-muted/30 p-4">
            {detail}
          </TableCell>
        </TableRow>
      ) : null}
    </Fragment>
  );
}

function StateRow({ columnCount, children }: { columnCount: number; children: React.ReactNode }) {
  return (
    <TableRow className="hover:bg-transparent">
      <TableCell colSpan={columnCount} className="h-56 whitespace-normal">
        {children}
      </TableCell>
    </TableRow>
  );
}

function LoadingRows({
  columnIds,
  messages,
}: {
  columnIds: readonly string[];
  messages: DataTableMessages;
}) {
  return (
    <>
      {loadingRowIds.map((rowId, row) => (
        <TableRow key={`loading-${rowId}`}>
          {columnIds.map((columnId, column) => (
            <TableCell key={`loading-${rowId}-${columnId}`}>
              <Skeleton className="h-5 w-full max-w-48" />
              {row === 0 && column === 0 ? (
                <span className="sr-only">{messages.loading}</span>
              ) : null}
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  );
}

function DataTableFooter<TData>({
  table,
  source,
  messages,
}: {
  table: ReturnType<typeof useReactTable<TData>>;
  source: DataTableDefinition<TData>['source'];
  messages: DataTableMessages;
}) {
  const visible = table.getRowModel().rows.length;
  const total =
    source.mode === 'page'
      ? source.rowCount
      : source.mode === 'infinite'
        ? (source.totalRowCount ?? source.data.length)
        : table.getFilteredRowModel().rows.length;
  const pageCount = Math.max(table.getPageCount(), 1);
  const page = Math.min(table.getState().pagination.pageIndex + 1, pageCount);
  const pageSizeOptions =
    source.mode === 'page'
      ? (source.pageSizeOptions ?? defaultPageSizeOptions)
      : source.mode === 'client' && source.pagination
        ? (source.pagination.pageSizeOptions ?? defaultPageSizeOptions)
        : defaultPageSizeOptions;
  const numbered =
    source.mode === 'page' || (source.mode === 'client' && source.pagination !== false);
  const pages = pageWindow(page - 1, pageCount);

  return (
    <footer
      data-slot="data-table-footer"
      className="flex min-h-12 shrink-0 flex-wrap items-center justify-between gap-2 border-t border-border bg-card px-3 py-2"
    >
      <span className="text-xs text-muted-foreground">{messages.rowStatus(visible, total)}</span>
      {numbered ? (
        <>
          <div className="flex items-center gap-1">
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label={messages.firstPage}
              disabled={!table.getCanPreviousPage()}
              onClick={() => table.firstPage()}
            >
              <ChevronFirst aria-hidden />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label={messages.previousPage}
              disabled={!table.getCanPreviousPage()}
              onClick={() => table.previousPage()}
            >
              <ChevronLeft aria-hidden />
            </Button>
            {pages.map((index) => (
              <Button
                key={index}
                type="button"
                variant={index === page - 1 ? 'secondary' : 'ghost'}
                size="icon-sm"
                aria-current={index === page - 1 ? 'page' : undefined}
                aria-label={messages.pageStatus(index + 1, pageCount)}
                onClick={() => table.setPageIndex(index)}
              >
                {index + 1}
              </Button>
            ))}
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label={messages.nextPage}
              disabled={!table.getCanNextPage()}
              onClick={() => table.nextPage()}
            >
              <ChevronRight aria-hidden />
            </Button>
            <Button
              type="button"
              variant="ghost"
              size="icon-sm"
              aria-label={messages.lastPage}
              disabled={!table.getCanNextPage()}
              onClick={() => table.lastPage()}
            >
              <ChevronLast aria-hidden />
            </Button>
          </div>
          <div className="flex items-center gap-2">
            <span className="hidden text-xs text-muted-foreground sm:inline">
              {messages.rowsPerPage}
            </span>
            <Select
              value={String(table.getState().pagination.pageSize)}
              onValueChange={(value) => value && table.setPageSize(Number(value))}
            >
              <SelectTrigger size="sm" aria-label={messages.rowsPerPage}>
                <SelectValue>{String(table.getState().pagination.pageSize)}</SelectValue>
              </SelectTrigger>
              <SelectContent align="end">
                {pageSizeOptions.map((size) => (
                  <SelectItem key={size} value={String(size)}>
                    {size}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <span className="hidden text-xs text-muted-foreground md:inline">
              {messages.pageStatus(page, pageCount)}
            </span>
          </div>
        </>
      ) : source.mode === 'infinite' ? (
        <div className="ml-auto flex items-center gap-2">
          {source.hasNextPage ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={source.isFetchingNextPage}
              onClick={() => void source.fetchNextPage()}
            >
              {source.isFetchingNextPage ? (
                <LoaderCircle className="animate-spin" aria-hidden />
              ) : null}
              {source.isFetchingNextPage ? messages.loadingMore : messages.loadMore}
            </Button>
          ) : (
            <span className="text-xs text-muted-foreground">{messages.endOfList}</span>
          )}
        </div>
      ) : null}
    </footer>
  );
}

function pageWindow(pageIndex: number, pageCount: number): number[] {
  const count = Math.min(pageCount, 5);
  const start = Math.max(0, Math.min(pageIndex - Math.floor(count / 2), pageCount - count));
  return Array.from({ length: count }, (_, index) => start + index);
}

function pinnedColumnStyle<TData>(column: Column<TData, unknown>): React.CSSProperties {
  const pinned = column.getIsPinned();
  if (!pinned) return {};
  return {
    position: 'sticky',
    left: pinned === 'left' ? column.getStart('left') : undefined,
    right: pinned === 'right' ? column.getAfter('right') : undefined,
    zIndex: 10,
    boxShadow:
      pinned === 'left' && column.getIsLastColumn('left')
        ? '-4px 0 4px -4px var(--border) inset'
        : pinned === 'right' && column.getIsFirstColumn('right')
          ? '4px 0 4px -4px var(--border) inset'
          : undefined,
  };
}
