import {
  type Column,
  type ColumnDef,
  flexRender,
  functionalUpdate,
  getCoreRowModel,
  getExpandedRowModel,
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
  Pin,
  PinOff,
  RefreshCw,
  TriangleAlert,
} from 'lucide-react';
import {
  Fragment,
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { AsyncButton } from '@/components/shared/AsyncButton';
import {
  persistentItemHighlight,
  transientItemHighlight,
} from '@/components/shared/interactionStates';
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
import { usePendingVisibility } from '@/hooks/usePendingVisibility';
import { cn } from '@/lib/utils';
import { axisStyles } from '@/theme.generated';
import { DataTableToolbar } from './DataTableToolbar';
import {
  countFilterConditions,
  createEmptyFilterExpression,
  filterData,
  pruneFilterExpression,
} from './filtering';
import { createDataTableValueFormatter, type DataTableValueFormatter } from './formatting';
import { dataTableCheckboxHitArea, dataTableTargetGeometry } from './geometry';
import type {
  DataTableCellDefinition,
  DataTableDefinition,
  DataTableMessages,
  DataTableQueryState,
} from './types';

const selectionColumnId = '__selection';
const defaultPageSizeOptions = [10, 20, 50, 100] as const;
const loadingRowIds = ['one', 'two', 'three', 'four', 'five', 'six'] as const;
const compactTableViewportWidth = 768;

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
  const [viewportWidth, setViewportWidth] = useState(0);
  const showInitialLoading = usePendingVisibility(Boolean(definition.loading));
  const initialLoading = !definition.error && (Boolean(definition.loading) || showInitialLoading);
  const valueFormatter = useMemo(
    () => createDataTableValueFormatter(definition.locale, messages),
    [definition.locale, messages],
  );

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
    const configured = definition.columns.map<ColumnDef<TData, unknown>>((column) =>
      column.cell
        ? column
        : {
            ...column,
            cell: ({ getValue }) => (
              <DataTableFormattedValue
                value={getValue()}
                definition={column.meta.cell}
                formatter={valueFormatter}
              />
            ),
          },
    );
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
        meta: { label: messages.selectRow, cell: { kind: 'action' }, searchable: false },
        header: ({ table }) => (
          <Checkbox
            aria-label={messages.selectAllRows}
            checked={table.getIsAllPageRowsSelected()}
            indeterminate={table.getIsSomePageRowsSelected()}
            className={dataTableCheckboxHitArea}
            onCheckedChange={(checked) => table.toggleAllPageRowsSelected(Boolean(checked))}
          />
        ),
        cell: ({ row }) => (
          <Checkbox
            aria-label={messages.selectRow}
            checked={row.getIsSelected()}
            disabled={!row.getCanSelect()}
            className={dataTableCheckboxHitArea}
            onCheckedChange={(checked) => row.toggleSelected(Boolean(checked))}
          />
        ),
      },
      ...configured,
    ];
  }, [definition.columns, definition.enableRowSelection, messages, valueFormatter]);
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
    getSortedRowModel: clientMode ? getSortedRowModel() : undefined,
    getGroupedRowModel: clientMode ? getGroupedRowModel() : undefined,
    getExpandedRowModel: getExpandedRowModel(),
    getPaginationRowModel: clientNumberedPagination ? getPaginationRowModel() : undefined,
    manualFiltering: true,
    manualSorting: !clientMode,
    manualGrouping: !clientMode,
    manualPagination: source.mode === 'page',
    rowCount: source.mode === 'page' ? source.rowCount : undefined,
    enableMultiSort: definition.enableMultiSort ?? true,
    enableGrouping: definition.grouping ?? false,
    enableColumnResizing: definition.enableColumnResizing ?? false,
    columnResizeMode: 'onChange',
    enableRowSelection: definition.enableRowSelection,
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

  useLayoutEffect(() => {
    const viewport = scrollRef.current;
    if (!viewport) return;
    const updateWidth = () => setViewportWidth(viewport.clientWidth);
    updateWidth();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(updateWidth);
    observer.observe(viewport);
    return () => observer.disconnect();
  }, []);

  const selectedRows = table.getSelectedRowModel().flatRows;
  const hasRows = rows.length > 0;
  const renderVirtualRows = virtualized && hasRows && !initialLoading && !definition.error;
  const hasQuery = Boolean(query.globalFilter) || countFilterConditions(query.filterExpression) > 0;
  const visibleColumnCount = Math.max(table.getVisibleLeafColumns().length, 1);
  const columnLayout = createColumnLayout(table.getVisibleLeafColumns(), viewportWidth);

  return (
    <section
      aria-label={definition.ariaLabel}
      aria-busy={initialLoading || undefined}
      data-slot="data-table"
      data-mode={source.mode}
      className={cn(
        'flex h-full min-h-0 min-w-0 flex-col overflow-hidden border border-border bg-card',
        axisStyles.radius.flat,
        axisStyles.elevation.none,
      )}
    >
      <DataTableToolbar
        table={table}
        messages={messages}
        globalSearch={definition.globalSearch ?? false}
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

      <div
        ref={scrollRef}
        onScroll={fetchMoreIfNeeded}
        data-slot="data-table-viewport"
        data-horizontal-overflow={columnLayout.allowsHorizontalOverflow ? 'compact' : 'fitted'}
        className="relative min-h-0 flex-1 overflow-auto overscroll-contain"
      >
        <Table
          containerClassName="overflow-visible"
          className={cn('table-fixed', virtualized && 'grid')}
          style={{ width: columnLayout.tableWidth, minWidth: '100%' }}
        >
          {!virtualized ? (
            <colgroup>
              {table.getVisibleLeafColumns().map((column) => (
                <col key={column.id} style={{ width: columnLayout.width(column) }} />
              ))}
            </colgroup>
          ) : null}
          <TableHeader
            className={cn('sticky top-0 bg-card', axisStyles.layer.sticky, virtualized && 'grid')}
          >
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id} className={cn(virtualized && 'flex w-full')}>
                {headerGroup.headers.map((header) => (
                  <TableHead
                    key={header.id}
                    colSpan={header.colSpan}
                    data-align={isEndAligned(header.column) ? 'end' : 'start'}
                    aria-sort={
                      header.column.getIsSorted() === 'asc'
                        ? 'ascending'
                        : header.column.getIsSorted() === 'desc'
                          ? 'descending'
                          : undefined
                    }
                    style={{
                      width: columnLayout.width(header.column),
                      ...pinnedColumnStyle(header.column, columnLayout),
                    }}
                    className={cn(
                      'relative overflow-hidden bg-card first:pl-3',
                      isEndAligned(header.column) && 'text-right',
                      virtualized && 'flex shrink-0 items-center',
                    )}
                  >
                    {header.isPlaceholder ? null : (
                      <DataTableColumnHeader
                        column={header.column}
                        messages={messages}
                        align={isEndAligned(header.column) ? 'end' : 'start'}
                      >
                        {header.column.columnDef.meta?.label ??
                          flexRender(header.column.columnDef.header, header.getContext())}
                      </DataTableColumnHeader>
                    )}
                    {header.column.getCanResize() ? (
                      <div className="absolute inset-y-0 right-0 flex items-center">
                        <Button
                          type="button"
                          variant="ghost"
                          size="icon-xs"
                          aria-label={`${header.column.columnDef.meta?.label ?? header.column.id}: resize`}
                          data-slot="data-table-resizer"
                          data-resizing={header.column.getIsResizing()}
                          onDoubleClick={() => header.column.resetSize()}
                          onMouseDown={header.getResizeHandler()}
                          onTouchStart={header.getResizeHandler()}
                          className="cursor-col-resize touch-none"
                        />
                      </div>
                    ) : null}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody
            className={cn(virtualized && 'relative grid')}
            style={renderVirtualRows ? { height: rowVirtualizer.getTotalSize() } : undefined}
          >
            {initialLoading ? (
              <LoadingRows
                columns={table.getVisibleLeafColumns()}
                columnLayout={columnLayout}
                messages={messages}
                visible={showInitialLoading}
                virtualized={virtualized}
              />
            ) : definition.error ? (
              <StateRow
                columnCount={visibleColumnCount}
                tableWidth={columnLayout.tableWidth}
                virtualized={virtualized}
              >
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
                      <Button
                        type="button"
                        variant="outline"
                        className={dataTableTargetGeometry}
                        onClick={definition.onRetry}
                      >
                        <RefreshCw aria-hidden />
                        {messages.retry}
                      </Button>
                    </EmptyContent>
                  ) : null}
                </Empty>
              </StateRow>
            ) : hasRows ? (
              renderVirtualRows ? (
                rowVirtualizer.getVirtualItems().map((virtualRow) => {
                  const row = rows[virtualRow.index];
                  return (
                    <DataRow
                      key={row.id}
                      row={row}
                      messages={messages}
                      valueFormatter={valueFormatter}
                      renderDetail={definition.renderDetail}
                      columnLayout={columnLayout}
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
                    valueFormatter={valueFormatter}
                    renderDetail={definition.renderDetail}
                    columnLayout={columnLayout}
                  />
                ))
              )
            ) : (
              <StateRow
                columnCount={visibleColumnCount}
                tableWidth={columnLayout.tableWidth}
                virtualized={virtualized}
              >
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
      </div>

      <DataTableFooter table={table} source={source} messages={messages} />
    </section>
  );
}

function DataTableColumnHeader<TData>({
  column,
  messages,
  align,
  children,
}: {
  column: Column<TData, unknown>;
  messages: DataTableMessages;
  align: 'start' | 'end';
  children: React.ReactNode;
}) {
  const sorted = column.getIsSorted();
  const configurable = column.getCanHide() || column.getCanPin();
  return (
    <div
      className={cn(
        'group relative flex w-full min-w-0 items-center overflow-hidden',
        align === 'end' && 'justify-end',
      )}
    >
      {column.getCanSort() ? (
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className={cn(
            dataTableTargetGeometry,
            '-ml-2 flex-1 gap-1 overflow-hidden px-2',
            align === 'end' ? 'ml-auto justify-end' : 'justify-start',
          )}
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
          <span data-slot="data-table-column-label" className="truncate">
            {children}
          </span>
          {sorted === 'asc' ? (
            <ArrowUp aria-hidden />
          ) : sorted === 'desc' ? (
            <ArrowDown aria-hidden />
          ) : (
            <ArrowUpDown aria-hidden />
          )}
        </Button>
      ) : (
        <span
          data-slot="data-table-column-label"
          className={cn('min-w-0 flex-1 truncate', align === 'end' && 'ml-auto')}
        >
          {children}
        </span>
      )}
      {configurable ? (
        <DropdownMenu>
          <DropdownMenuTrigger
            render={
              <Button
                type="button"
                variant="ghost"
                size="icon-xs"
                className={cn(
                  dataTableTargetGeometry,
                  'pointer-events-none absolute right-0 top-1/2 -translate-y-1/2 bg-card opacity-0 transition-opacity group-focus-within:pointer-events-auto group-focus-within:opacity-100 group-hover:pointer-events-auto group-hover:opacity-100',
                )}
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
  valueFormatter,
  renderDetail,
  columnLayout,
  virtual,
}: {
  row: Row<TData>;
  messages: DataTableMessages;
  valueFormatter: DataTableValueFormatter;
  renderDetail?: (row: Row<TData>) => React.ReactNode;
  columnLayout: DataTableColumnLayout<TData>;
  virtual?: { start: number; measure: (element: Element | null) => void };
}) {
  const visibleCells = row.getVisibleCells();
  const rowRef = useRef<HTMLTableRowElement | null>(null);
  const [multiline, setMultiline] = useState(false);
  const setRowRef = useCallback(
    (element: HTMLTableRowElement | null) => {
      rowRef.current = element;
      virtual?.measure(element);
    },
    [virtual],
  );
  useLayoutEffect(() => {
    const element = rowRef.current;
    if (!element) return;
    const updateLayout = () => {
      const next = [
        ...element.querySelectorAll<HTMLElement>('[data-slot="data-table-cell-content"]'),
      ].some(hasMultipleTextLines);
      setMultiline((current) => (current === next ? current : next));
    };
    updateLayout();
    if (typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(updateLayout);
    observer.observe(element);
    const mutationObserver =
      typeof MutationObserver === 'undefined' ? undefined : new MutationObserver(updateLayout);
    mutationObserver?.observe(element, { childList: true, characterData: true, subtree: true });
    return () => {
      observer.disconnect();
      mutationObserver?.disconnect();
    };
  }, []);
  const detail =
    row.getIsExpanded() && renderDetail && !row.getIsGrouped() ? renderDetail(row) : null;
  return (
    <Fragment>
      <TableRow
        ref={setRowRef}
        data-index={virtual ? row.index : undefined}
        data-row-layout={multiline ? 'multiline' : 'single-line'}
        className={cn(
          'bg-card',
          multiline && '[&_[data-slot=data-table-record-action]]:items-start',
          transientItemHighlight,
          row.getIsSelected() && persistentItemHighlight,
          virtual && 'absolute flex w-full',
        )}
        style={virtual ? { transform: `translateY(${virtual.start}px)` } : undefined}
      >
        {visibleCells.map((cell, index) => {
          const grouped = cell.getIsGrouped();
          const aggregated = cell.getIsAggregated();
          const isAggregateCell = cell.getIsPlaceholder();
          const canExpand =
            index === (visibleCells[0]?.column.id === selectionColumnId ? 1 : 0) &&
            row.getCanExpand();
          const cellDefinition = cell.column.columnDef.meta?.cell ?? { kind: 'text' as const };
          const endAligned = cellDefinition.kind === 'number';
          const renderer = aggregated
            ? (cell.column.columnDef.aggregatedCell ?? cell.column.columnDef.cell)
            : cell.column.columnDef.cell;
          const renderedValue = renderer ? (
            flexRender(renderer, cell.getContext())
          ) : (
            <DataTableFormattedValue
              value={cell.getValue()}
              definition={cellDefinition}
              formatter={valueFormatter}
            />
          );
          return (
            <TableCell
              key={cell.id}
              data-align={endAligned ? 'end' : 'start'}
              data-cell-kind={cellDefinition.kind}
              style={{
                width: columnLayout.width(cell.column),
                ...pinnedColumnStyle(cell.column, columnLayout),
              }}
              className={cn(
                'overflow-hidden bg-inherit py-1 first:pl-3',
                multiline ? 'align-top' : 'align-middle',
                endAligned && 'text-right tabular-nums',
                virtual && 'flex shrink-0',
                virtual && (multiline ? 'items-start' : 'items-center'),
              )}
            >
              <div
                data-slot="data-table-cell-content"
                className={cn(
                  'flex min-w-0 overflow-hidden',
                  multiline ? 'items-start' : 'items-center',
                  endAligned && 'w-full justify-end text-right',
                  axisStyles.spacing.gap.inline,
                  axisStyles.density.minHeight.touchTarget,
                  axisStyles.density.minHeight.compactControlAtSmall,
                )}
                style={canExpand && row.depth > 0 ? { paddingLeft: row.depth * 16 } : undefined}
              >
                {grouped || canExpand ? (
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon-xs"
                    className={dataTableTargetGeometry}
                    aria-label={row.getIsExpanded() ? messages.collapseRow : messages.expandRow}
                    onClick={row.getToggleExpandedHandler()}
                  >
                    {row.getIsExpanded() ? <ChevronUp aria-hidden /> : <ChevronRight aria-hidden />}
                  </Button>
                ) : null}
                {grouped ? (
                  <>
                    {renderedValue}
                    <span className="text-xs text-muted-foreground">({row.subRows.length})</span>
                  </>
                ) : aggregated ? (
                  renderedValue
                ) : isAggregateCell ? null : (
                  renderedValue
                )}
              </div>
            </TableCell>
          );
        })}
      </TableRow>
      {detail ? (
        <TableRow>
          <TableCell colSpan={visibleCells.length}>{detail}</TableCell>
        </TableRow>
      ) : null}
    </Fragment>
  );
}

function DataTableFormattedValue({
  value,
  definition,
  formatter,
}: {
  value: unknown;
  definition: DataTableCellDefinition;
  formatter: DataTableValueFormatter;
}) {
  return (
    <span
      data-slot="data-table-value"
      className={cn(
        'min-w-0 max-w-full',
        definition.kind === 'list' ? 'whitespace-normal break-words' : 'truncate whitespace-nowrap',
        (definition.kind === 'identifier' ||
          definition.kind === 'version' ||
          definition.kind === 'revision') &&
          'font-mono text-xs',
        (definition.kind === 'number' ||
          definition.kind === 'date' ||
          definition.kind === 'dateTime') &&
          'tabular-nums',
      )}
    >
      {formatter.format(value, definition)}
    </span>
  );
}

function StateRow({
  columnCount,
  tableWidth,
  virtualized,
  children,
}: {
  columnCount: number;
  tableWidth: number | string;
  virtualized: boolean;
  children: React.ReactNode;
}) {
  return (
    <TableRow className={cn(virtualized && 'flex w-full')}>
      <TableCell
        colSpan={columnCount}
        className={cn('h-56 whitespace-normal first:pl-3', virtualized && 'block shrink-0')}
        style={virtualized ? { width: tableWidth } : undefined}
      >
        {children}
      </TableCell>
    </TableRow>
  );
}

function LoadingRows<TData>({
  columns,
  columnLayout,
  messages,
  visible,
  virtualized,
}: {
  columns: readonly Column<TData, unknown>[];
  columnLayout: DataTableColumnLayout<TData>;
  messages: DataTableMessages;
  visible: boolean;
  virtualized: boolean;
}) {
  return (
    <>
      {loadingRowIds.map((rowId, row) => (
        <TableRow
          key={`loading-${rowId}`}
          className={cn(!visible && 'invisible', virtualized && 'flex w-full')}
        >
          {columns.map((columnDefinition, column) => (
            <TableCell
              key={`loading-${rowId}-${columnDefinition.id}`}
              data-align={isEndAligned(columnDefinition) ? 'end' : 'start'}
              className={cn(
                'py-1 align-middle first:pl-3',
                isEndAligned(columnDefinition) && 'text-right',
                virtualized && 'flex shrink-0 items-center',
              )}
              style={virtualized ? { width: columnLayout.width(columnDefinition) } : undefined}
            >
              <div
                className={cn(
                  'flex min-w-0 items-center',
                  isEndAligned(columnDefinition) && 'justify-end',
                  axisStyles.density.minHeight.touchTarget,
                  axisStyles.density.minHeight.compactControlAtSmall,
                )}
              >
                <Skeleton className="h-5 w-full max-w-48" />
              </div>
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

function isEndAligned<TData>(column: Column<TData, unknown>): boolean {
  return column.columnDef.meta?.cell.kind === 'number';
}

interface DataTableColumnLayout<TData> {
  tableWidth: number | string;
  allowsHorizontalOverflow: boolean;
  width: (column: Column<TData, unknown>) => number;
  left: (column: Column<TData, unknown>) => number | undefined;
  right: (column: Column<TData, unknown>) => number | undefined;
}

function createColumnLayout<TData>(
  columns: readonly Column<TData, unknown>[],
  viewportWidth: number,
): DataTableColumnLayout<TData> {
  const preferredWidth = columns.reduce((total, column) => total + column.getSize(), 0);
  const allowsHorizontalOverflow = viewportWidth > 0 && viewportWidth < compactTableViewportWidth;
  const fitToViewport = viewportWidth > 0 && !allowsHorizontalOverflow;
  const widths = new Map<string, number>();

  if (!fitToViewport) {
    for (const column of columns) widths.set(column.id, column.getSize());
  } else {
    const fixedColumns = columns.filter(isFixedWidthColumn);
    const flexibleColumns = columns.filter((column) => !isFixedWidthColumn(column));
    const fixedWidth = fixedColumns.reduce((total, column) => total + column.getSize(), 0);
    const flexibleBudget = Math.max(viewportWidth - fixedWidth, 0);
    const flexiblePreferredWidth = flexibleColumns.reduce(
      (total, column) => total + column.getSize(),
      0,
    );
    for (const column of fixedColumns) widths.set(column.id, column.getSize());
    for (const column of flexibleColumns) {
      widths.set(
        column.id,
        flexiblePreferredWidth > 0
          ? (flexibleBudget * column.getSize()) / flexiblePreferredWidth
          : 0,
      );
    }
  }

  const leftOffsets = new Map<string, number>();
  let left = 0;
  for (const column of columns.filter((column) => column.getIsPinned() === 'left')) {
    leftOffsets.set(column.id, left);
    left += widths.get(column.id) ?? column.getSize();
  }
  const rightOffsets = new Map<string, number>();
  let right = 0;
  for (const column of columns.filter((column) => column.getIsPinned() === 'right').reverse()) {
    rightOffsets.set(column.id, right);
    right += widths.get(column.id) ?? column.getSize();
  }

  return {
    tableWidth: fitToViewport ? '100%' : Math.max(preferredWidth, viewportWidth),
    allowsHorizontalOverflow,
    width: (column) => widths.get(column.id) ?? column.getSize(),
    left: (column) => leftOffsets.get(column.id),
    right: (column) => rightOffsets.get(column.id),
  };
}

function isFixedWidthColumn<TData>(column: Column<TData, unknown>): boolean {
  const { minSize, maxSize } = column.columnDef;
  return minSize !== undefined && maxSize !== undefined && minSize === maxSize;
}

function hasMultipleTextLines(element: HTMLElement): boolean {
  const candidates = [element, ...element.querySelectorAll<HTMLElement>('*')].filter(
    (candidate) => candidate.childElementCount === 0,
  );
  const lineTops: number[] = [];
  for (const candidate of candidates) {
    if (!candidate.textContent?.trim() || candidate.closest('.sr-only,[aria-hidden="true"]')) {
      continue;
    }
    for (const rect of Array.from(candidate.getClientRects())) {
      if (rect.width <= 0 || rect.height <= 0) continue;
      if (!lineTops.some((top) => Math.abs(top - rect.top) < 2)) lineTops.push(rect.top);
      if (lineTops.length > 1) return true;
    }
    const lineHeight = Number.parseFloat(getComputedStyle(candidate).lineHeight);
    if (Number.isFinite(lineHeight) && candidate.scrollHeight > lineHeight * 1.5) return true;
  }
  return false;
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
        : table.getCoreRowModel().rows.length;
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
              className={dataTableTargetGeometry}
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
              className={dataTableTargetGeometry}
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
                variant={index === page - 1 ? 'default' : 'outline'}
                size="icon-sm"
                className={dataTableTargetGeometry}
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
              className={dataTableTargetGeometry}
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
              className={dataTableTargetGeometry}
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
              <SelectTrigger
                size="sm"
                aria-label={messages.rowsPerPage}
                className={dataTableTargetGeometry}
              >
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
            <AsyncButton
              type="button"
              variant="outline"
              size="sm"
              icon={<ChevronDown />}
              pending={source.isFetchingNextPage}
              pendingLabel={messages.loadingMore}
              className={dataTableTargetGeometry}
              onClick={() => void source.fetchNextPage()}
            >
              {messages.loadMore}
            </AsyncButton>
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

function pinnedColumnStyle<TData>(
  column: Column<TData, unknown>,
  layout: DataTableColumnLayout<TData>,
): React.CSSProperties {
  const pinned = column.getIsPinned();
  if (!pinned) return {};
  return {
    position: 'sticky',
    left: pinned === 'left' ? layout.left(column) : undefined,
    right: pinned === 'right' ? layout.right(column) : undefined,
    zIndex: 10,
    boxShadow:
      pinned === 'left' && column.getIsLastColumn('left')
        ? '-4px 0 4px -4px var(--border) inset'
        : pinned === 'right' && column.getIsFirstColumn('right')
          ? '4px 0 4px -4px var(--border) inset'
          : undefined,
  };
}
