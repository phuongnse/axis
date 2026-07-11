import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import {
  countFilterConditions,
  createEmptyFilterExpression,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
  type DataTableMessages,
  type DataTableQueryState,
} from '@/components/shared/data-table';
import { Button } from '@/components/ui/button';

interface Item {
  id: string;
  name: string;
  amount: number;
  created: string;
  occurredAt: string;
  active: boolean;
  status: 'Open' | 'Closed';
  department: string;
  children?: Item[];
}

const items: Item[] = [
  {
    id: '1',
    name: 'Alpha',
    amount: 10,
    created: '2026-01-01',
    occurredAt: '2026-01-01T10:00:00Z',
    active: true,
    status: 'Open',
    department: 'Finance',
  },
  {
    id: '2',
    name: 'Beta',
    amount: 20,
    created: '2026-02-01',
    occurredAt: '2026-02-01T10:00:00Z',
    active: false,
    status: 'Closed',
    department: 'Operations',
  },
  {
    id: '3',
    name: 'Gamma',
    amount: 30,
    created: '2026-03-01',
    occurredAt: '2026-03-01T10:00:00Z',
    active: true,
    status: 'Open',
    department: 'Finance',
  },
];

const messages: DataTableMessages = {
  searchLabel: 'Search records',
  searchPlaceholder: 'Search records',
  filters: 'Filters',
  addCondition: 'Add condition',
  addFilterGroup: 'Add group',
  removeCondition: 'Remove condition',
  removeFilterGroup: 'Remove group',
  filterAnd: 'All conditions',
  filterOr: 'Any condition',
  filterIncomplete: 'Complete the condition.',
  selectFilterField: 'Select field',
  selectFilterOperator: 'Select operator',
  selectFilterValue: 'Select value',
  filterOperators: {
    eq: 'Equals',
    ne: 'Does not equal',
    contains: 'Contains',
    notContains: 'Does not contain',
    startsWith: 'Starts with',
    endsWith: 'Ends with',
    lt: 'Less than',
    lte: 'Less than or equal',
    gt: 'Greater than',
    gte: 'Greater than or equal',
    between: 'Between',
    notBetween: 'Not between',
    in: 'One of',
    notIn: 'Not one of',
    containsAny: 'Contains any',
    containsAll: 'Contains all',
    notContainsAny: 'Contains none',
    isEmpty: 'Is empty',
    isNotEmpty: 'Is not empty',
  },
  filterBy: (column) => `Filter by ${column}`,
  columns: 'Columns',
  grouping: 'Group',
  clearFilters: 'Clear filters',
  emptyTitle: 'No records',
  emptyDescription: 'No records are available.',
  noResultsTitle: 'No matches',
  noResultsDescription: 'Change the query.',
  loading: 'Loading records',
  errorTitle: 'Unable to load',
  errorDescription: 'Try again.',
  retry: 'Retry',
  sortAscending: 'Sort ascending',
  sortDescending: 'Sort descending',
  clearSorting: 'Clear sorting',
  hideColumn: 'Hide column',
  pinLeft: 'Pin left',
  pinRight: 'Pin right',
  unpin: 'Unpin',
  minimum: 'Minimum',
  maximum: 'Maximum',
  trueValue: 'Yes',
  falseValue: 'No',
  allValues: 'All values',
  rowsPerPage: 'Rows per page',
  pageStatus: (page, count) => `Page ${page} of ${count}`,
  rowStatus: (visible, total) => `${visible} of ${total} rows`,
  selectedStatus: (selected, total) => `${selected} of ${total} selected`,
  firstPage: 'First page',
  previousPage: 'Previous page',
  nextPage: 'Next page',
  lastPage: 'Last page',
  loadMore: 'Load more',
  loadingMore: 'Loading more',
  endOfList: 'All loaded',
  selectAllRows: 'Select page',
  selectRow: 'Select row',
  expandRow: 'Expand row',
  collapseRow: 'Collapse row',
};

const columns: DataTableColumnDef<Item>[] = [
  {
    accessorKey: 'name',
    meta: {
      label: 'Name',
      searchable: true,
      filter: { kind: 'text' },
    },
    cell: ({ row }) => row.original.name,
  },
  {
    accessorKey: 'amount',
    meta: { label: 'Amount', filter: { kind: 'number' } },
    cell: ({ row }) => row.original.amount,
  },
  {
    accessorKey: 'created',
    meta: { label: 'Created', filter: { kind: 'date' } },
    cell: ({ row }) => row.original.created,
  },
  {
    accessorKey: 'occurredAt',
    meta: { label: 'Occurred', filter: { kind: 'dateTime' } },
    cell: ({ row }) => row.original.occurredAt,
  },
  {
    accessorKey: 'active',
    meta: { label: 'Active', filter: { kind: 'boolean' } },
    cell: ({ row }) => (row.original.active ? 'Active' : 'Inactive'),
  },
  {
    accessorKey: 'status',
    enableGrouping: true,
    meta: {
      label: 'Status',
      filter: {
        kind: 'singleChoice',
        options: [
          { value: 'Open', label: 'Open' },
          { value: 'Closed', label: 'Closed' },
        ],
      },
    },
    cell: ({ row }) => row.original.status,
  },
  {
    accessorKey: 'department',
    enableGrouping: true,
    aggregationFn: 'count',
    meta: { label: 'Department' },
    cell: ({ row }) => row.original.department,
    aggregatedCell: ({ getValue }) => `${String(getValue())} records`,
  },
];

function clientDefinition(
  overrides: Partial<DataTableDefinition<Item>> = {},
): DataTableDefinition<Item> {
  return {
    ariaLabel: 'Records',
    source: { mode: 'client', data: items, pagination: { pageSize: 2 } },
    columns,
    messages,
    getRowId: (row) => row.id,
    ...overrides,
  };
}

describe('DataTable', () => {
  it('renders, searches, sorts, and uses numbered client pagination', async () => {
    const user = userEvent.setup();
    render(
      <DataTable
        definition={clientDefinition({
          renderToolbarActions: ({ rows, queryState }) => (
            <Button type="button" variant="outline">
              Export {rows.length} rows with {countFilterConditions(queryState.filterExpression)}{' '}
              filters
            </Button>
          ),
        })}
      />,
    );

    const table = screen.getByRole('region', { name: 'Records' });
    const toolbarActions = table.querySelector('[data-slot="data-table-toolbar-actions"]');
    expect(toolbarActions).not.toBeNull();
    expect(
      within(toolbarActions as HTMLElement).getByRole('button', {
        name: 'Export 2 rows with 0 filters',
      }),
    ).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: /Name/ })).toBeInTheDocument();
    expect(within(table).getByText('Alpha')).toBeInTheDocument();
    expect(within(table).queryByText('Gamma')).not.toBeInTheDocument();

    await user.click(within(table).getByRole('button', { name: 'Next page' }));
    expect(within(table).getByText('Gamma')).toBeInTheDocument();
    expect(within(table).getByRole('button', { name: 'Page 2 of 2' })).toHaveAttribute(
      'aria-current',
      'page',
    );

    await user.type(within(table).getByLabelText('Search records'), 'beta');
    expect(within(table).getByText('Beta')).toBeInTheDocument();
    expect(within(table).queryByText('Alpha')).not.toBeInTheDocument();

    await user.clear(within(table).getByLabelText('Search records'));
    await user.click(within(table).getByRole('button', { name: 'Name: Sort ascending' }));
    expect(
      within(table).getByRole('button', { name: 'Name: Sort descending' }),
    ).toBeInTheDocument();
  });

  it('evaluates nested typed filters and clears hidden-column conditions', async () => {
    const user = userEvent.setup();
    render(
      <DataTable
        definition={clientDefinition({
          initialState: {
            filterExpression: {
              id: 'root',
              combinator: 'and',
              items: [
                { id: 'amount', fieldId: 'amount', operator: 'gte', value: 20 },
                {
                  id: 'choice',
                  combinator: 'or',
                  items: [
                    { id: 'closed', fieldId: 'status', operator: 'eq', value: 'Closed' },
                    { id: 'gamma', fieldId: 'name', operator: 'startsWith', value: 'Gamma' },
                  ],
                },
              ],
            },
          },
        })}
      />,
    );

    const table = screen.getByRole('region', { name: 'Records' });
    expect(within(table).queryByText('Alpha')).not.toBeInTheDocument();
    expect(within(table).getByText('Beta')).toBeInTheDocument();
    expect(within(table).getByText('Gamma')).toBeInTheDocument();
    fireEvent.click(within(table).getByRole('button', { name: 'Columns' }));
    await user.click(await screen.findByRole('menuitemcheckbox', { name: 'Status' }));

    expect(within(table).queryByText('Beta')).not.toBeInTheDocument();
    expect(within(table).getByText('Gamma')).toBeInTheDocument();
    expect(within(table).queryByRole('columnheader', { name: 'Status' })).not.toBeInTheDocument();
  });

  it('derives type-specific editors from visible column metadata', async () => {
    const user = userEvent.setup();
    render(<DataTable definition={clientDefinition()} />);
    const table = screen.getByRole('region', { name: 'Records' });
    await user.click(within(table).getByRole('button', { name: 'Filters' }));
    await user.click(screen.getByRole('button', { name: 'Add condition' }));

    await user.click(screen.getByTestId('fields'));
    await user.click(await screen.findByRole('option', { name: 'Amount' }));
    expect(screen.getByTestId('value-editor')).toHaveAttribute('type', 'number');

    await user.click(screen.getByTestId('fields'));
    await user.click(await screen.findByRole('option', { name: 'Created' }));
    expect(screen.getByTestId('value-editor')).toHaveAttribute('type', 'date');
    expect(screen.getByRole('button', { name: 'Add group' })).toBeInTheDocument();
  });

  it('emits structured query and numbered pagination state in manual page mode', async () => {
    const user = userEvent.setup();
    const onQueryStateChange = vi.fn();
    const onPaginationChange = vi.fn();
    function ManualTable() {
      const [queryState, setQueryState] = useState<DataTableQueryState>({
        globalFilter: '',
        filterExpression: createEmptyFilterExpression(),
        sorting: [],
        grouping: [],
      });
      return (
        <DataTable
          definition={{
            ariaLabel: 'Server records',
            source: {
              mode: 'page',
              data: [items[0]],
              pagination: { pageIndex: 0, pageSize: 1 },
              rowCount: 3,
              onPaginationChange,
            },
            columns,
            messages,
            getRowId: (row) => row.id,
            queryState,
            onQueryStateChange: (next) => {
              setQueryState(next);
              onQueryStateChange(next);
            },
          }}
        />
      );
    }
    render(<ManualTable />);

    await user.type(screen.getByLabelText('Search records'), 'missing');
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(onQueryStateChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ globalFilter: 'missing' }),
    );
    await user.click(screen.getByRole('button', { name: 'Next page' }));
    expect(onPaginationChange).toHaveBeenCalledWith({ pageIndex: 1, pageSize: 1 });
  });

  it('supports grouping, expansion, selection, and consumer bulk actions', async () => {
    const user = userEvent.setup();
    render(
      <DataTable
        definition={clientDefinition({
          grouping: true,
          enableRowSelection: true,
          initialState: { grouping: ['department'], expanded: true },
          renderDetail: (row) => <div>{row.original.name} detail</div>,
          renderBulkActions: (rows, clear) => (
            <Button type="button" onClick={clear}>
              Archive {rows.length}
            </Button>
          ),
        })}
      />,
    );

    expect(screen.getByText(/Finance/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Collapse row' }).length).toBeGreaterThan(0);
    const rowCheckboxes = screen.getAllByRole('checkbox', { name: 'Select row' });
    await user.click(rowCheckboxes[0]);
    expect(screen.getByRole('button', { name: 'Archive 1' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Archive 1' }));
    expect(screen.queryByRole('button', { name: 'Archive 1' })).not.toBeInTheDocument();
  });

  it('supports infinite loading state and progressive fetch callbacks', async () => {
    const fetchNextPage = vi.fn();
    render(
      <DataTable
        definition={{
          ariaLabel: 'Infinite records',
          source: {
            mode: 'infinite',
            data: items.slice(0, 2),
            hasNextPage: true,
            isFetchingNextPage: false,
            fetchNextPage,
            totalRowCount: 10,
          },
          columns,
          messages,
          getRowId: (row) => row.id,
        }}
      />,
    );

    expect(screen.getByText('2 of 10 rows')).toBeInTheDocument();
    await waitFor(() => expect(fetchNextPage).toHaveBeenCalled());
    fireEvent.click(screen.getByRole('button', { name: 'Load more' }));
    expect(fetchNextPage.mock.calls.length).toBeGreaterThanOrEqual(2);
  });
});
