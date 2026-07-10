import type {
  ColumnDef,
  ColumnOrderState,
  ColumnPinningState,
  ExpandedState,
  GroupingState,
  PaginationState,
  Row,
  RowSelectionState,
  SortingState,
  VisibilityState,
} from '@tanstack/react-table';
import type { ReactNode } from 'react';

export interface DataTableFilterOption {
  value: string;
  label: string;
}

interface DataTableFilterBase<TData> {
  getValue?: (row: TData) => unknown;
}

export type DataTableFilterDefinition<TData> =
  | (DataTableFilterBase<TData> & { kind: 'text'; placeholder?: string })
  | (DataTableFilterBase<TData> & { kind: 'number' })
  | (DataTableFilterBase<TData> & { kind: 'date' })
  | (DataTableFilterBase<TData> & { kind: 'dateTime' })
  | (DataTableFilterBase<TData> & { kind: 'boolean' })
  | (DataTableFilterBase<TData> & {
      kind: 'singleChoice' | 'multiChoice';
      options: readonly DataTableFilterOption[];
    });

export type DataTableFilterOperator =
  | 'eq'
  | 'ne'
  | 'contains'
  | 'notContains'
  | 'startsWith'
  | 'endsWith'
  | 'lt'
  | 'lte'
  | 'gt'
  | 'gte'
  | 'between'
  | 'notBetween'
  | 'in'
  | 'notIn'
  | 'containsAny'
  | 'containsAll'
  | 'notContainsAny'
  | 'isEmpty'
  | 'isNotEmpty';

export type DataTableFilterValue = string | number | boolean | readonly string[] | null;

export interface DataTableFilterCondition {
  id: string;
  fieldId: string;
  operator: DataTableFilterOperator;
  value: DataTableFilterValue;
}

export interface DataTableFilterGroup {
  id: string;
  combinator: 'and' | 'or';
  items: readonly (DataTableFilterCondition | DataTableFilterGroup)[];
}

export interface DataTableColumnMeta<TData> {
  label: string;
  filter?: DataTableFilterDefinition<TData>;
  searchable?: boolean;
  searchValue?: (row: TData) => unknown;
}

declare module '@tanstack/react-table' {
  interface ColumnMeta<TData, TValue> extends DataTableColumnMeta<TData> {}
}

export type DataTableColumnDef<TData> = ColumnDef<TData, unknown>;

export interface DataTableQueryState {
  globalFilter: string;
  filterExpression: DataTableFilterGroup;
  sorting: SortingState;
  grouping: GroupingState;
}

export interface DataTableInitialState {
  globalFilter?: string;
  filterExpression?: DataTableFilterGroup;
  sorting?: SortingState;
  grouping?: GroupingState;
  columnVisibility?: VisibilityState;
  columnOrder?: ColumnOrderState;
  columnPinning?: ColumnPinningState;
  expanded?: ExpandedState;
  rowSelection?: RowSelectionState;
}

export interface DataTableToolbarActionContext<TData> {
  rows: readonly Row<TData>[];
  selectedRows: readonly Row<TData>[];
  queryState: DataTableQueryState;
  clearSelection: () => void;
}

export interface DataTableClientSource<TData> {
  mode: 'client';
  data: readonly TData[];
  pagination?:
    | false
    | {
        pageSize?: number;
        pageSizeOptions?: readonly number[];
      };
}

export interface DataTablePageSource<TData> {
  mode: 'page';
  data: readonly TData[];
  pagination: PaginationState;
  rowCount: number;
  pageSizeOptions?: readonly number[];
  onPaginationChange: (pagination: PaginationState) => void;
}

export interface DataTableInfiniteSource<TData> {
  mode: 'infinite';
  data: readonly TData[];
  hasNextPage: boolean;
  isFetchingNextPage: boolean;
  fetchNextPage: () => unknown;
  totalRowCount?: number;
  virtualize?: boolean;
  estimateRowHeight?: number;
  fetchThreshold?: number;
}

export type DataTableSource<TData> =
  | DataTableClientSource<TData>
  | DataTablePageSource<TData>
  | DataTableInfiniteSource<TData>;

export interface DataTableMessages {
  searchLabel: string;
  searchPlaceholder: string;
  filters: string;
  addCondition: string;
  addFilterGroup: string;
  removeCondition: string;
  removeFilterGroup: string;
  filterAnd: string;
  filterOr: string;
  filterIncomplete: string;
  selectFilterField: string;
  selectFilterOperator: string;
  selectFilterValue: string;
  filterOperators: Record<DataTableFilterOperator, string>;
  filterBy: (column: string) => string;
  columns: string;
  grouping: string;
  clearFilters: string;
  emptyTitle: string;
  emptyDescription: string;
  noResultsTitle: string;
  noResultsDescription: string;
  loading: string;
  errorTitle: string;
  errorDescription: string;
  retry: string;
  sortAscending: string;
  sortDescending: string;
  clearSorting: string;
  hideColumn: string;
  pinLeft: string;
  pinRight: string;
  unpin: string;
  minimum: string;
  maximum: string;
  trueValue: string;
  falseValue: string;
  allValues: string;
  rowsPerPage: string;
  pageStatus: (page: number, pageCount: number) => string;
  rowStatus: (visible: number, total: number) => string;
  selectedStatus: (selected: number, total: number) => string;
  firstPage: string;
  previousPage: string;
  nextPage: string;
  lastPage: string;
  loadMore: string;
  loadingMore: string;
  endOfList: string;
  selectAllRows: string;
  selectRow: string;
  expandRow: string;
  collapseRow: string;
}

export interface DataTableDefinition<TData> {
  ariaLabel: string;
  source: DataTableSource<TData>;
  columns: readonly DataTableColumnDef<TData>[];
  messages: DataTableMessages;
  getRowId: (row: TData) => string;
  initialState?: DataTableInitialState;
  queryState?: DataTableQueryState;
  onQueryStateChange?: (state: DataTableQueryState) => void;
  globalSearch?: boolean;
  columnControls?: boolean;
  grouping?: boolean;
  enableColumnResizing?: boolean;
  enableMultiSort?: boolean;
  enableRowSelection?: boolean | ((row: Row<TData>) => boolean);
  getSubRows?: (row: TData) => TData[] | undefined;
  renderDetail?: (row: Row<TData>) => ReactNode;
  renderToolbarActions?: (context: DataTableToolbarActionContext<TData>) => ReactNode;
  renderBulkActions?: (rows: readonly Row<TData>[], clearSelection: () => void) => ReactNode;
  loading?: boolean;
  error?: boolean;
  onRetry?: () => void;
}
