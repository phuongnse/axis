import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  countFilterConditions,
  createEmptyFilterExpression,
  createResourceMetadataColumns,
  DataTable,
  type DataTableColumnDef,
  type DataTableDefinition,
  type DataTableMessages,
  type DataTableQueryState,
  DataTableRecordAction,
} from '@/components/shared/data-table';
import { PageAction } from '@/components/shared/PageLayout';
import { axisStyles } from '@/theme.generated';

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
    amount: 1234.5,
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
  emptyValue: 'N/A',
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
      cell: { kind: 'text' },
      searchable: true,
      filter: { kind: 'text' },
    },
  },
  {
    accessorKey: 'amount',
    meta: { label: 'Amount', cell: { kind: 'number' }, filter: { kind: 'number' } },
  },
  {
    accessorKey: 'created',
    meta: { label: 'Created', cell: { kind: 'date' }, filter: { kind: 'date' } },
  },
  {
    accessorKey: 'occurredAt',
    meta: { label: 'Occurred', cell: { kind: 'dateTime' }, filter: { kind: 'dateTime' } },
  },
  {
    accessorKey: 'active',
    meta: { label: 'Active', cell: { kind: 'boolean' }, filter: { kind: 'boolean' } },
  },
  {
    accessorKey: 'status',
    enableGrouping: true,
    meta: {
      label: 'Status',
      cell: { kind: 'status' },
      filter: {
        kind: 'singleChoice',
        options: [
          { value: 'Open', label: 'Open' },
          { value: 'Closed', label: 'Closed' },
        ],
      },
    },
  },
  {
    accessorKey: 'department',
    enableGrouping: true,
    aggregationFn: 'count',
    meta: { label: 'Department', cell: { kind: 'text' } },
    aggregatedCell: ({ getValue }) => `${String(getValue())} records`,
  },
];

function clientDefinition(
  overrides: Partial<DataTableDefinition<Item>> = {},
): DataTableDefinition<Item> {
  return {
    ariaLabel: 'Records',
    locale: 'en-US',
    source: { mode: 'client', data: items, pagination: { pageSize: 2 } },
    columns,
    messages,
    getRowId: (row) => row.id,
    ...overrides,
  };
}

describe('DataTable', () => {
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('uses the shared N/A placeholder for a missing cell value', async () => {
    const missingValueColumns: DataTableColumnDef<Item>[] = [
      {
        id: 'missing',
        accessorFn: () => undefined,
        meta: { label: 'Missing', cell: { kind: 'text' } },
      },
    ];
    render(
      <DataTable
        definition={clientDefinition({
          source: { mode: 'client', data: [items[0]], pagination: { pageSize: 1 } },
          columns: missingValueColumns,
        })}
      />,
    );

    const cell = (await screen.findByText('N/A')).closest('td');
    await waitFor(() => expect(cell).toHaveAttribute('data-cell-kind', 'text'));
  });

  it('fits columns to a regular viewport and reserves horizontal overflow for compact widths', () => {
    const originalClientWidth = Object.getOwnPropertyDescriptor(
      HTMLElement.prototype,
      'clientWidth',
    );
    let viewportWidth = 1024;
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return this.getAttribute('data-slot') === 'data-table-viewport' ? viewportWidth : 0;
      },
    });

    const sizingColumns: DataTableColumnDef<Item>[] = [
      {
        accessorKey: 'name',
        size: 700,
        minSize: 300,
        meta: { label: 'Name', cell: { kind: 'text' } },
      },
      {
        accessorKey: 'status',
        size: 100,
        minSize: 120,
        meta: { label: 'Status', cell: { kind: 'status' } },
      },
      {
        accessorKey: 'department',
        size: 100,
        minSize: 120,
        meta: { label: 'Department', cell: { kind: 'text' } },
      },
    ];

    try {
      for (viewportWidth of [768, 1024, 1280]) {
        const desktop = render(
          <DataTable definition={clientDefinition({ columns: sizingColumns })} />,
        );
        const desktopViewport = desktop.container.querySelector<HTMLElement>(
          '[data-slot="data-table-viewport"]',
        );
        expect(desktopViewport).toHaveAttribute('data-horizontal-overflow', 'fitted');
        expect(desktopViewport?.querySelector('table')).toHaveStyle({ width: '100%' });
        const widths = [...(desktopViewport?.querySelectorAll('col') ?? [])].map((column) =>
          Number.parseFloat((column as HTMLElement).style.width),
        );
        expect(widths).toHaveLength(3);
        expect(widths[0]).toBeGreaterThanOrEqual(300);
        expect(widths[1]).toBeGreaterThanOrEqual(120);
        expect(widths[2]).toBeGreaterThanOrEqual(120);
        expect(
          desktopViewport?.querySelector('[data-slot="data-table-column-label"]'),
        ).toHaveAttribute('title', 'Name');
        expect(desktopViewport?.querySelector('[data-slot="data-table-value"]')).toHaveAttribute(
          'title',
          'Alpha',
        );
        desktop.unmount();
      }

      viewportWidth = 390;
      const compact = render(<DataTable definition={clientDefinition()} />);
      const compactViewport = compact.container.querySelector<HTMLElement>(
        '[data-slot="data-table-viewport"]',
      );
      const compactTable = compactViewport?.querySelector('table');
      expect(compactViewport).toHaveAttribute('data-horizontal-overflow', 'compact');
      expect(Number.parseFloat(compactTable?.style.width ?? '0')).toBeGreaterThan(viewportWidth);
      compact.unmount();
    } finally {
      if (originalClientWidth) {
        Object.defineProperty(HTMLElement.prototype, 'clientWidth', originalClientWidth);
      } else {
        Reflect.deleteProperty(HTMLElement.prototype, 'clientWidth');
      }
    }
  });

  it('sorts actor metadata by localized display name with deterministic missing values', async () => {
    type MetadataRow = {
      id: string;
      name: string;
      metadata?: {
        revision: number;
        createdBy?: { displayName?: string | null };
        createdAt: string;
        modifiedBy?: { displayName?: string | null };
        modifiedAt: string;
      };
    };
    const metadataRows: MetadataRow[] = [
      {
        id: 'first',
        name: 'First record',
        metadata: {
          revision: 1,
          createdBy: { displayName: 'Ada Lovelace' },
          createdAt: '2026-01-01T00:00:00Z',
          modifiedBy: { displayName: 'Zed User' },
          modifiedAt: '2026-01-02T00:00:00Z',
        },
      },
      {
        id: 'second',
        name: 'Second record',
        metadata: {
          revision: 2,
          createdBy: { displayName: 'Zed User' },
          createdAt: '2026-01-01T00:00:00Z',
          modifiedBy: { displayName: 'Ada Lovelace' },
          modifiedAt: '2026-01-02T00:00:00Z',
        },
      },
      { id: 'missing', name: 'Missing actor' },
    ];
    const metadataColumns: DataTableColumnDef<MetadataRow>[] = [
      {
        accessorKey: 'name',
        meta: { label: 'Name', cell: { kind: 'text' } },
      },
      ...createResourceMetadataColumns<MetadataRow>(
        {
          revision: 'Revision',
          createdBy: 'Created by',
          createdAt: 'Created at',
          modifiedBy: 'Modified by',
          modifiedAt: 'Modified at',
        },
        { locale: 'en-US' },
      ),
    ];
    const user = userEvent.setup();
    const rendered = render(
      <DataTable
        definition={{
          ariaLabel: 'Metadata records',
          locale: 'en-US',
          source: { mode: 'client', data: metadataRows, pagination: false },
          columns: metadataColumns,
          messages,
          getRowId: (row) => row.id,
        }}
      />,
    );
    const rowNames = () =>
      [...rendered.container.querySelectorAll('tbody tr')].map(
        (row) => row.querySelector('td')?.textContent,
      );

    await user.click(screen.getByRole('button', { name: 'Created by: Sort ascending' }));
    await waitFor(() =>
      expect(rowNames()).toEqual(['First record', 'Second record', 'Missing actor']),
    );

    await user.click(screen.getByRole('button', { name: 'Modified by: Sort ascending' }));
    await waitFor(() =>
      expect(rowNames()).toEqual(['Second record', 'First record', 'Missing actor']),
    );
  });

  it('formats semantic values and reserves end alignment for quantities', async () => {
    type TypedRow = {
      id: string;
      version: number;
      revision: number;
      actor: { displayName: string };
      actorKind: string;
      quantity: number;
    };
    const typedColumns: DataTableColumnDef<TypedRow>[] = [
      {
        accessorKey: 'version',
        meta: { label: 'Version', cell: { kind: 'version' } },
      },
      {
        accessorKey: 'revision',
        meta: { label: 'Revision', cell: { kind: 'revision' } },
      },
      {
        accessorKey: 'actor',
        meta: { label: 'Modified by', cell: { kind: 'actor' } },
      },
      {
        accessorKey: 'actorKind',
        meta: { label: 'Invalid actor source', cell: { kind: 'actor' } },
      },
      {
        accessorKey: 'quantity',
        meta: { label: 'Quantity', cell: { kind: 'number' } },
      },
    ];
    const row: TypedRow = {
      id: 'typed',
      version: 7,
      revision: 12,
      actor: { displayName: 'Ada Lovelace' },
      actorKind: 'Human',
      quantity: 1234,
    };

    render(
      <DataTable
        definition={{
          ariaLabel: 'Typed values',
          locale: 'en-US',
          source: { mode: 'client', data: [row], pagination: false },
          columns: typedColumns,
          messages,
          getRowId: (value) => value.id,
        }}
      />,
    );

    for (const value of ['v7', 'r12', 'Ada Lovelace']) {
      const cell = (await screen.findByText(value)).closest('td');
      expect(cell).toHaveAttribute('data-align', 'start');
      expect(cell).toHaveClass('align-middle');
      expect(cell?.querySelector('[data-slot="data-table-cell-content"]')).toHaveClass(
        'items-center',
      );
    }
    expect((await screen.findByText('v7')).closest('tr')).toHaveAttribute(
      'data-row-layout',
      'single-line',
    );
    expect((await screen.findByText('1,234')).closest('td')).toHaveAttribute('data-align', 'end');
    expect(screen.queryByText('Human')).not.toBeInTheDocument();
    expect(screen.getAllByText('N/A')).toHaveLength(1);
  });

  it('keeps record actions on the cell content edge without shrinking their target', async () => {
    const user = userEvent.setup();
    const onOpen = vi.fn();
    render(<DataTableRecordAction onClick={onOpen}>Customer</DataTableRecordAction>);

    const action = screen.getByRole('button', { name: 'Customer' });
    expect(action).toHaveAttribute('data-slot', 'data-table-record-action');
    expect(action).toHaveClass(
      'justify-start',
      '-ml-px',
      'px-0',
      'text-left',
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minWidth.touchTarget,
      axisStyles.density.minHeight.compactControlAtSmall,
      axisStyles.density.minWidth.compactControlAtSmall,
    );
    expect(action).not.toHaveClass('px-2.5');
    expect(action.querySelector('[data-slot="data-table-record-action-label"]')).toHaveClass(
      'min-w-0',
      'truncate',
    );

    await user.click(action);
    expect(onOpen).toHaveBeenCalledOnce();
  });

  it('top-aligns the whole row to the first line when one typed cell wraps', async () => {
    vi.spyOn(HTMLElement.prototype, 'getClientRects').mockImplementation(function () {
      const line = { width: 40, height: 20, top: 0 } as DOMRect;
      return this.textContent?.includes('Alpha, Beta')
        ? ([line, { ...line, top: 20 }] as unknown as DOMRectList)
        : ([line] as unknown as DOMRectList);
    });
    type WrappedRow = { id: string; name: string; tags: string[] };
    const wrappedColumns: DataTableColumnDef<WrappedRow>[] = [
      {
        accessorKey: 'name',
        meta: { label: 'Name', cell: { kind: 'text' } },
        cell: ({ getValue }) => (
          <DataTableRecordAction onClick={() => undefined}>
            {String(getValue())}
          </DataTableRecordAction>
        ),
      },
      {
        accessorKey: 'tags',
        meta: { label: 'Tags', cell: { kind: 'list' } },
      },
    ];

    render(
      <DataTable
        definition={{
          ariaLabel: 'Wrapped records',
          locale: 'en-US',
          source: {
            mode: 'client',
            data: [{ id: 'wrapped', name: 'Customer', tags: ['Alpha', 'Beta'] }],
            pagination: false,
          },
          columns: wrappedColumns,
          messages,
          getRowId: (row) => row.id,
        }}
      />,
    );

    const row = screen.getByRole('row', { name: /Customer Alpha, Beta/ });
    await waitFor(() => expect(row).toHaveAttribute('data-row-layout', 'multiline'));
    for (const cell of row.querySelectorAll('td')) {
      expect(cell).toHaveClass('align-top');
      expect(cell.querySelector('[data-slot="data-table-cell-content"]')).toHaveClass(
        'items-start',
      );
    }
  });

  it('reserves initial rows, delays skeletons, and does not use background refresh as loading', async () => {
    vi.useFakeTimers();
    const definition = clientDefinition({ loading: true });
    const { rerender } = render(<DataTable definition={definition} />);
    const table = screen.getByRole('region', { name: 'Records' });

    expect(table).toHaveAttribute('aria-busy', 'true');
    expect(table.querySelector('tbody tr')).toHaveClass('invisible');

    await act(async () => vi.advanceTimersByTimeAsync(300));
    expect(table.querySelector('tbody tr')).not.toHaveClass('invisible');

    rerender(<DataTable definition={clientDefinition({ loading: false })} />);
    expect(screen.queryByText('Alpha')).not.toBeInTheDocument();

    await act(async () => vi.advanceTimersByTimeAsync(400));
    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(table).not.toHaveAttribute('aria-busy');
  });

  it('renders, sorts, and uses numbered client pagination without implicit search', async () => {
    const user = userEvent.setup();
    render(
      <DataTable
        definition={clientDefinition({
          renderToolbarActions: ({ rows, queryState }) => (
            <PageAction type="button" variant="outline">
              Export {rows.length} rows with {countFilterConditions(queryState.filterExpression)}{' '}
              filters
            </PageAction>
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
    const nameHeader = within(table).getByRole('columnheader', { name: /Name/ });
    expect(nameHeader).not.toHaveAttribute('aria-sort');
    const nameSort = within(nameHeader).getByRole('button', { name: 'Name: Sort ascending' });
    expect(nameSort).toHaveClass(
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minWidth.touchTarget,
      axisStyles.density.minHeight.compactControlAtSmall,
      axisStyles.density.minWidth.compactControlAtSmall,
    );
    expect(nameHeader).toHaveClass('first:pl-3');
    expect(nameHeader.querySelector('[data-slot="data-table-column-label"]')).not.toBeNull();
    const alphaCell = within(table).getByText('Alpha').closest('td');
    expect(alphaCell).toHaveClass('py-1', 'align-middle', 'first:pl-3');
    const alphaCellContent = alphaCell?.querySelector('[data-slot="data-table-cell-content"]');
    expect(alphaCellContent).toHaveClass(
      'items-center',
      axisStyles.spacing.gap.inline,
      axisStyles.density.minHeight.touchTarget,
      axisStyles.density.minHeight.compactControlAtSmall,
    );
    const amountHeader = within(table).getByRole('columnheader', { name: /Amount/ });
    const amountCell = within(table).getByText('10').closest('td');
    expect(amountHeader).toHaveAttribute('data-align', 'end');
    expect(amountHeader).toHaveClass('text-right');
    expect(amountCell).toHaveAttribute('data-cell-kind', 'number');
    expect(amountCell).toHaveAttribute('data-align', 'end');
    expect(amountCell).toHaveClass('text-right', 'tabular-nums');
    expect(within(table).getByText('1,234.5')).toBeInTheDocument();
    expect(within(table).getByText('Jan 1, 2026')).toBeInTheDocument();
    expect(within(table).getByText('Yes')).toBeInTheDocument();
    expect(within(table).queryByText('Gamma')).not.toBeInTheDocument();

    expect(table.querySelectorAll('[data-slot="table"]')).toHaveLength(1);
    const viewport = table.querySelector<HTMLElement>('[data-slot="data-table-viewport"]');
    expect(viewport).not.toBeNull();
    if (viewport) {
      viewport.scrollLeft = 64;
      fireEvent.scroll(viewport);
      expect(viewport.scrollLeft).toBe(64);
    }

    await user.click(within(table).getByRole('button', { name: 'Next page' }));
    expect(within(table).getByText('Gamma')).toBeInTheDocument();
    expect(within(table).getByRole('button', { name: 'Page 2 of 2' })).toHaveAttribute(
      'aria-current',
      'page',
    );
    expect(within(table).getByRole('button', { name: 'Page 2 of 2' })).toHaveClass('bg-primary');
    expect(within(table).getByRole('button', { name: 'Page 1 of 2' })).toHaveClass('border-border');

    expect(within(table).queryByLabelText('Search records')).not.toBeInTheDocument();
    await user.click(nameSort);
    expect(nameHeader).toHaveAttribute('aria-sort', 'ascending');
    expect(
      within(table).getByRole('button', { name: 'Name: Sort descending' }),
    ).toBeInTheDocument();
    await user.click(within(table).getByRole('button', { name: 'Name: Sort descending' }));
    expect(nameHeader).toHaveAttribute('aria-sort', 'descending');
    await user.click(within(table).getByRole('button', { name: 'Name: Clear sorting' }));
    expect(nameHeader).not.toHaveAttribute('aria-sort');
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
            locale: 'en-US',
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
            globalSearch: true,
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
            <PageAction type="button" onClick={clear}>
              Archive {rows.length}
            </PageAction>
          ),
        })}
      />,
    );

    expect(screen.getByText(/Finance/)).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Collapse row' }).length).toBeGreaterThan(0);
    const rowCheckboxes = screen.getAllByRole('checkbox', { name: 'Select row' });
    await user.click(rowCheckboxes[0]);
    const selectedRow = rowCheckboxes[0].closest('tr');
    expect(selectedRow).toHaveClass('bg-secondary', 'hover:bg-secondary');
    expect(selectedRow?.querySelector('td')).toHaveClass('bg-inherit');
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
          locale: 'en-US',
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
