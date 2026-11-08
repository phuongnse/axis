import { DeleteOutlined } from '@ant-design/icons'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Button } from 'antd'
import { useState, type HTMLAttributes } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { chooseOption, tabTo } from '../test/antd'
import { dialog, filterTexts } from '../test/tableFilter'
import { AxisTable, type AxisColumn, type AxisRowAction, type AxisTableProps } from './AxisTable'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import type { TableQuery } from './tableQuery'
import { narrowWidthQuery } from './useNarrowWidth'

interface Row {
  id: string
  name: string | null
  amount: number | null
  group?: boolean
  note?: string
}

const texts = {
  'shell.table.refresh': 'Refresh',
  'shell.table.rowActions': 'Actions',
  'shell.table.selected': '{count} selected',
  'shell.table.range': '{from}–{to} of {total}',
  'shell.table.search': 'Search',
  'shell.table.columns': 'Columns',
  'shell.table.columnsApply': 'Apply',
  'shell.table.columnsReset': 'Reset',
  'shell.table.columnsMoveUp': 'Move up',
  'shell.table.columnsMoveDown': 'Move down',
  'shell.form.cancel': 'Cancel',
  'shell.table.selectRow': 'Select row',
  'row.archive': 'Archive',
  'row.delete': 'Delete',
  'row.open': 'Open',
  'row.close': 'Close',
  'shell.empty.noData': 'No data yet.',
  ...filterTexts,
}
const catalogs = [{ texts, fallbackTexts: texts }]

const rows: Row[] = [
  { id: '1', name: 'Buy paper', amount: 12 },
  { id: '2', name: null, amount: null },
]

const nameColumn: AxisColumn<Row> = { key: 'name', title: 'Name', dataIndex: 'name', kind: 'freeText' }
const noteColumn: AxisColumn<Row> = { key: 'note', title: 'Note', dataIndex: 'name', kind: 'freeText' }
const amountColumn: AxisColumn<Row> = { key: 'amount', title: 'Amount', dataIndex: 'amount', kind: 'fixed' }

function renderTable(props: Partial<AxisTableProps<Row>> = {}) {
  return render(
    <TextProvider catalogs={catalogs} development={false}>
      <AxisTable<Row>
        columns={[nameColumn, amountColumn]}
        rowKey={(row) => row.id}
        dataSource={rows}
        loading={false}
        emptyText="Nothing here"
        pagination={{ current: 1, pageSize: 20, total: rows.length, pageSizeOptions: [10, 20] }}
        toolbar={{}}
        {...props}
      />
    </TextProvider>,
  )
}

const deleteAction: AxisRowAction = {
  key: 'delete',
  labelKey: 'row.delete',
  icon: <DeleteOutlined />,
  destructive: true,
  onClick: vi.fn(),
}
const openAction: AxisRowAction = { key: 'open', labelKey: 'row.open', onClick: vi.fn() }
const closeAction: AxisRowAction = { key: 'close', labelKey: 'row.close', onClick: vi.fn() }

describe('AxisTable', () => {
  it('keeps its column headers and draws the empty state in its body, with the line in the muted text colour', () => {
    const { container } = renderTable({ dataSource: [], pagination: false })

    expect(screen.getByRole('columnheader', { name: 'Name' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Amount' })).toBeInTheDocument()
    expect(container.querySelector('.ant-table-tbody .ant-empty-image')).not.toBeNull()
    expect(screen.getByText('Nothing here')).toHaveStyle({ color: designTokensFor('light').colour.textMuted })
  })

  it('draws "No data yet." when the table names no empty text', () => {
    renderTable({ dataSource: [], pagination: false, emptyText: undefined })

    expect(screen.getByText('No data yet.')).toHaveStyle({ color: designTokensFor('light').colour.textMuted })
  })

  describe('columns', () => {
    it('fits a fixed column to its content without wrapping', () => {
      renderTable()

      for (const cell of [
        screen.getByRole('columnheader', { name: 'Amount' }),
        screen.getByRole('cell', { name: '12' }),
      ]) {
        expect(cell).toHaveStyle({ width: '1%', whiteSpace: 'nowrap' })
      }
    })

    it('shares the width between free-text columns in equal parts and wraps their text at 320 px', () => {
      renderTable({ columns: [nameColumn, noteColumn, amountColumn] })

      expect(screen.getByRole('columnheader', { name: 'Name' })).toHaveStyle({ width: '50%' })
      expect(screen.getByRole('columnheader', { name: 'Note' })).toHaveStyle({ width: '50%' })
      const cell = screen.getAllByRole('cell', { name: 'Buy paper' })[0]
      expect(cell.firstElementChild).toHaveStyle({ maxWidth: '320px', whiteSpace: 'normal', overflowWrap: 'anywhere' })
    })

    it('gives the last column the rest when no column is free text', () => {
      renderTable({
        columns: [
          { ...amountColumn, key: 'first' },
          { ...amountColumn, key: 'last', title: 'Last' },
        ],
      })

      expect(screen.getByRole('columnheader', { name: 'Last' }).style.width).toBe('')
      expect(screen.getAllByRole('columnheader', { name: 'Amount' })[0]).toHaveStyle({ width: '1%' })
    })

    it('keeps the styles a column sets for its cells', () => {
      renderTable({
        columns: [nameColumn, { ...amountColumn, onCell: () => ({ style: { fontVariantNumeric: 'tabular-nums' } }) }],
      })

      expect(screen.getByRole('cell', { name: '12' })).toHaveStyle({
        width: '1%',
        fontVariantNumeric: 'tabular-nums',
      })
    })

    it('shows the em dash for an empty cell, and nothing in a blank row', () => {
      renderTable({
        dataSource: [rows[1], { id: '3', name: null, amount: null, group: true }],
        blankRow: (row) => row.group === true,
      })

      expect(screen.getAllByText('—')).toHaveLength(2)
      const [, , groupRow] = screen.getAllByRole('row')
      expect(within(groupRow).queryByText('—')).not.toBeInTheDocument()
    })

    it('shows the em dash when a render gives nothing', () => {
      renderTable({ columns: [{ ...nameColumn, render: () => null }] })

      expect(screen.getAllByText('—')).toHaveLength(2)
    })
  })

  it('makes every body row and the header row as high as the tableRow size', () => {
    renderTable()

    const { tableRow } = designTokensFor('light').size
    const bodyRows = screen.getAllByRole('row').filter((row) => within(row).queryAllByRole('cell').length > 0)
    expect(bodyRows).toHaveLength(rows.length)
    for (const row of bodyRows) {
      expect(row).toHaveStyle({ height: `${tableRow}px` })
    }
    expect(screen.getAllByRole('columnheader')[0].closest('tr')).toHaveStyle({ height: `${tableRow}px` })
  })

  it('shows the empty text when there are no rows', () => {
    renderTable({ dataSource: [] })

    expect(screen.getByText('Nothing here')).toBeInTheDocument()
  })

  it("shows what a paged table shows of how many, with the number in the locale's format", () => {
    const many = Array.from({ length: 20 }, (_, index) => ({ id: String(index), name: `Row ${index}`, amount: index }))
    renderTable({ dataSource: many, pagination: { current: 1, pageSize: 20, total: 143, pageSizeOptions: [10, 20] } })

    expect(screen.getByText('1–20 of 143')).toBeInTheDocument()
  })

  it('draws no search box for a table that holds its rows when no column gives searchText', () => {
    renderTable({ view: { query: { page: 1, pageSize: 20, sort: null }, onChange: vi.fn() }, pagination: undefined })

    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
  })

  it('lists every row on one page without paging when pagination is false', () => {
    const many = Array.from({ length: 25 }, (_, index) => ({ id: String(index), name: `Row ${index}`, amount: index }))
    renderTable({ dataSource: many, pagination: false })

    expect(screen.getAllByRole('row').filter((row) => within(row).queryAllByRole('cell').length > 0)).toHaveLength(25)
    expect(screen.queryByRole('list')).not.toBeInTheDocument()
  })

  describe('paging', () => {
    const many = Array.from({ length: 25 }, (_, index) => ({ id: String(index), name: `Row ${index}`, amount: index }))
    const bodyRows = () => screen.getAllByRole('row').filter((row) => within(row).queryAllByRole('cell').length > 0)

    it('lists every row and draws no pager when pagination is not given', () => {
      renderTable({ dataSource: many, pagination: undefined })

      expect(bodyRows()).toHaveLength(25)
      expect(screen.queryByRole('list')).not.toBeInTheDocument()
    })

    it('shows the page-size picker when page sizes are offered', () => {
      renderTable({ dataSource: many })

      expect(screen.getByRole('combobox')).toBeInTheDocument()
      expect(screen.getByRole('list')).toBeInTheDocument()
    })

    it('shows a pager with no picker when there is more than one page and no page sizes are offered', () => {
      renderTable({ pagination: { current: 1, pageSize: 20, total: 30 } })

      expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
      expect(screen.getByRole('list')).toBeInTheDocument()
    })

    it('draws no pager when there is no picker and one page', () => {
      renderTable({ pagination: { current: 1, pageSize: 20, total: 2 } })

      expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
      expect(screen.queryByRole('list')).not.toBeInTheDocument()
    })
  })

  it('renders with no toolbar and no loading state', () => {
    render(
      <TextProvider catalogs={catalogs} development={false}>
        <AxisTable<Row> columns={[nameColumn]} rowKey={(row) => row.id} dataSource={rows} emptyText="Nothing here" />
      </TextProvider>,
    )

    expect(screen.getByRole('table')).toBeInTheDocument()
    expect(screen.getByRole('table').closest('[aria-busy]')).toHaveAttribute('aria-busy', 'false')
    expect(screen.queryByRole('button', { name: 'Refresh' })).not.toBeInTheDocument()
  })

  it('puts testId on the outer element of the table', () => {
    renderTable({ testId: 'a-table' })

    expect(screen.getByTestId('a-table')).toContainElement(screen.getByRole('table'))
  })

  it('puts the attributes of rowAttributes on the row element', () => {
    renderTable({
      rowAttributes: (row) =>
        ({ 'data-testid': 'a-row', 'data-amount': row.amount ?? undefined }) as HTMLAttributes<HTMLElement>,
    })

    const found = screen.getAllByTestId('a-row')
    expect(found.map((row) => row.getAttribute('data-amount'))).toEqual(['12', null])
  })

  it('passes the index of each row to rowAttributes', () => {
    renderTable({
      rowAttributes: (_row, index) => ({ 'data-index': String(index) }) as HTMLAttributes<HTMLElement>,
    })

    const found = [...document.querySelectorAll('tbody tr[data-index]')]
    expect(found.map((row) => row.getAttribute('data-index'))).toEqual(['0', '1'])
  })

  it('shows the failure in place of the rows and keeps the toolbar', () => {
    renderTable({ failure: <p>Failed</p>, toolbar: { onRefresh: () => {} } })

    expect(screen.getByText('Failed')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument()
  })

  describe('row actions', () => {
    const actionsFor = (...actions: AxisRowAction[]) => ({
      configured: actions.length,
      actions: (row: Row) => (row.id === '2' ? null : actions),
    })

    it('draws an icon action by its name and a configured action by its text', () => {
      renderTable({ rowActions: actionsFor(deleteAction, openAction) })

      const row = screen.getByRole('row', { name: /Buy paper/ })
      expect(within(row).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
      expect(within(row).getByRole('button', { name: 'Open' })).toHaveTextContent('Open')
      expect(within(row).queryByRole('button', { name: 'Actions' })).not.toBeInTheDocument()
    })

    it('appends the name suffix of an icon action to its accessible name only', async () => {
      const user = userEvent.setup()
      renderTable({ rowActions: actionsFor({ ...deleteAction, nameSuffix: '7' }) })

      const button = screen.getByRole('button', { name: 'Delete 7' })
      expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
      await user.hover(button)
      expect(await screen.findByRole('tooltip')).toHaveTextContent(/^Delete$/)
    })

    it('names the actions column for screen readers, so its header is not empty', () => {
      renderTable({ rowActions: actionsFor(deleteAction) })

      expect(screen.getByRole('columnheader', { name: 'Actions' })).toBeInTheDocument()
    })

    it('draws nothing in a row that gives no actions', () => {
      renderTable({ rowActions: actionsFor(deleteAction) })

      const row = document.querySelector<HTMLElement>('[data-row-key="2"]')!
      expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    })

    it('folds three actions into one Actions menu', async () => {
      renderTable({ rowActions: actionsFor(deleteAction, openAction, closeAction) })

      const row = screen.getByRole('row', { name: /Buy paper/ })
      const buttons = within(row).getAllByRole('button')
      expect(buttons).toHaveLength(1)
      expect(buttons[0]).toHaveAccessibleName('Actions')

      await userEvent.click(buttons[0])
      expect(await screen.findByRole('menuitem', { name: 'Delete' })).toBeInTheDocument()
      await userEvent.click(screen.getByRole('menuitem', { name: 'Close' }))
      expect(closeAction.onClick).toHaveBeenCalled()
    })

    it('names the Actions button in a tooltip on keyboard focus', async () => {
      renderTable({ rowActions: actionsFor(deleteAction, openAction, closeAction) })

      await tabTo(screen.getByRole('button', { name: 'Actions' }))

      expect(await screen.findByRole('tooltip')).toHaveTextContent('Actions')
    })

    describe('at narrow width', () => {
      beforeEach(() => {
        vi.stubGlobal('matchMedia', (query: string) => ({
          matches: query === narrowWidthQuery,
          media: query,
          onchange: null,
          addListener() {},
          removeListener() {},
          addEventListener() {},
          removeEventListener() {},
          dispatchEvent: () => false,
        }))
      })

      it('folds two actions into one Actions menu', () => {
        renderTable({ rowActions: actionsFor(deleteAction, openAction) })

        const row = screen.getByRole('row', { name: /Buy paper/ })
        expect(within(row).getAllByRole('button')).toHaveLength(1)
        expect(within(row).getByRole('button', { name: 'Actions' })).toBeInTheDocument()
      })
    })
  })

  describe('bulk actions', () => {
    const archive = (onClick = vi.fn()) => ({ key: 'archive', labelKey: 'row.archive', onClick })

    it('shows no checkboxes without a bulk action', () => {
      renderTable()
      expect(screen.queryAllByRole('checkbox')).toHaveLength(0)
    })

    it('shows no checkboxes with row actions alone', () => {
      renderTable({ rowActions: { configured: 1, actions: () => [deleteAction] } })
      expect(screen.queryAllByRole('checkbox')).toHaveLength(0)
    })

    it('shows no bar until a row is selected', () => {
      renderTable({ bulkActions: [archive()] })

      expect(screen.getAllByRole('checkbox', { name: 'Select row' })).toHaveLength(rows.length)
      expect(screen.queryByText(/selected/)).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Archive' })).not.toBeInTheDocument()
    })

    it('counts the selected rows, runs the action on them and clears the selection', async () => {
      const user = userEvent.setup()
      const onClick = vi.fn()
      renderTable({ bulkActions: [archive(onClick)] })

      const bodyRows = screen.getAllByRole('row').slice(1)
      await user.click(within(bodyRows[0]).getByRole('checkbox'))
      await user.click(within(bodyRows[1]).getByRole('checkbox'))
      expect(screen.getByText('2 selected')).toBeInTheDocument()

      await user.click(screen.getByRole('button', { name: 'Archive' }))

      expect(onClick).toHaveBeenCalledWith([rows[0], rows[1]])
      expect(screen.queryByText(/selected/)).not.toBeInTheDocument()
    })

    it('keeps a row action in its row, and the bar holds the bulk action only', async () => {
      const user = userEvent.setup()
      renderTable({ rowActions: { configured: 1, actions: () => [deleteAction] }, bulkActions: [archive()] })

      const bodyRows = screen.getAllByRole('row').slice(1)
      await user.click(within(bodyRows[0]).getByRole('checkbox'))

      expect(screen.getByText('1 selected')).toBeInTheDocument()
      expect(within(bodyRows[0]).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
      const bar = screen.getByText('1 selected').parentElement as HTMLElement
      expect(within(bar).getAllByRole('button')).toHaveLength(1)
      expect(within(bar).getByRole('button', { name: 'Archive' })).toBeInTheDocument()
    })
  })

  describe('toolbar', () => {
    it('shows nothing above the rows when the table has no toolbar item', () => {
      renderTable()

      expect(screen.queryByRole('button', { name: 'Refresh' })).not.toBeInTheDocument()
    })

    it('puts the view first, then Refresh, the secondary actions and the main action last', () => {
      const { container } = renderTable({
        toolbar: {
          view: <input aria-label="Search" />,
          onRefresh: () => {},
          secondary: (
            <>
              <Button>Import</Button>
              <Button>Export</Button>
            </>
          ),
          main: <Button>Add</Button>,
        },
      })

      const refresh = screen.getByRole('button', { name: 'Refresh' })
      expect(
        within(refresh.parentElement!)
          .getAllByRole('button')
          .map((button) => button.textContent || button.getAttribute('aria-label')),
      ).toEqual(['Refresh', 'Import', 'Export', 'Add'])
      const order = [...container.querySelectorAll('input, button')].map(
        (element) => element.getAttribute('aria-label') ?? element.textContent,
      )
      expect(order.slice(0, 5)).toEqual(['Search', 'Refresh', 'Import', 'Export', 'Add'])
    })

    it('calls onRefresh from the Refresh button', async () => {
      const onRefresh = vi.fn()
      renderTable({ toolbar: { onRefresh } })

      await userEvent.click(screen.getByRole('button', { name: 'Refresh' }))

      expect(onRefresh).toHaveBeenCalledOnce()
    })

    it('puts what sits below the toolbar between it and the rows', () => {
      renderTable({ toolbar: { onRefresh: () => {} }, below: <p>Filters</p> })

      const refresh = screen.getByRole('button', { name: 'Refresh' })
      const filters = screen.getByText('Filters')
      const table = screen.getByRole('table')
      expect(refresh.compareDocumentPosition(filters) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(filters.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    })
  })

  it('marks the table busy while it loads', () => {
    const { container } = renderTable({ loading: true })

    expect(container.firstElementChild).toHaveAttribute('aria-busy', 'true')
  })

  it('shows the Filter button on a table paged on the server and applies the filter from page 1 without changing the rows', async () => {
    const onChange = vi.fn()
    const filterable: AxisColumn<Row> = {
      key: 'name',
      title: 'Name',
      dataIndex: 'name',
      kind: 'freeText',
      filter: { kind: 'text' },
    }
    renderTable({
      columns: [filterable, amountColumn],
      view: { query: { page: 3, pageSize: 20, sort: null }, onChange, total: 60 },
      pagination: undefined,
    })

    await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
    await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Operator' }))
    await chooseOption('contains')
    await userEvent.type(within(dialog()).getByRole('textbox', { name: 'Value' }), 'pa')
    await userEvent.click(within(dialog()).getByRole('button', { name: 'Apply' }))

    expect(onChange).toHaveBeenCalledWith({
      page: 1,
      pageSize: 20,
      sort: null,
      filter: { all: [{ field: 'name', op: 'contains', value: 'pa' }] },
    })
    expect(screen.getByRole('row', { name: /Buy paper/ })).toBeInTheDocument()
    expect(screen.getAllByRole('row').filter((row) => within(row).queryAllByRole('cell').length > 0)).toHaveLength(2)
  })

  describe('columns control', () => {
    const idColumn: AxisColumn<Row> = {
      key: 'name',
      title: 'Name',
      dataIndex: 'name',
      kind: 'freeText',
      searchText: (row) => row.name ?? '',
    }
    const hiddenNote: AxisColumn<Row> = {
      key: 'note',
      title: 'Note',
      dataIndex: 'note',
      kind: 'freeText',
      searchText: (row) => row.note ?? '',
      filter: { kind: 'text', value: (row) => row.note ?? null },
    }
    const arranged = [idColumn, amountColumn, hiddenNote]
    const noted: Row[] = [
      { id: '1', name: 'Buy paper', amount: 12, note: 'urgent' },
      { id: '2', name: 'Fix door', amount: 5, note: 'later' },
    ]
    const first: TableQuery = { page: 1, pageSize: 20, sort: null }

    /** Holds the view as the address would, with the Columns button on. */
    function Harness({ initial = first, onQuery }: { initial?: TableQuery; onQuery?: (query: TableQuery) => void }) {
      const [query, setQuery] = useState(initial)
      return (
        <TextProvider catalogs={catalogs} development={false}>
          <AxisTable<Row>
            columns={arranged}
            rowKey={(row) => row.id}
            dataSource={noted}
            view={{
              query,
              onChange: (next) => {
                setQuery(next)
                onQuery?.(next)
              },
              columns: true,
            }}
          />
        </TextProvider>
      )
    }

    const headers = () => screen.getAllByRole('columnheader').map((header) => header.textContent)
    const columnsDialog = () => screen.getByRole('dialog', { name: 'Columns' })

    it('hides a column and reorders the rest, keeping the identifying column first and always shown', async () => {
      render(<Harness />)
      expect(headers()).toEqual(['Name', 'Amount', 'Note'])

      await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
      const lines = within(columnsDialog())
      expect(lines.getByRole('checkbox', { name: 'Name' })).toBeChecked()
      expect(lines.getByRole('checkbox', { name: 'Name' })).toBeDisabled()
      expect(lines.queryByRole('button', { name: /Move (up|down) Name/ })).not.toBeInTheDocument()
      await userEvent.click(lines.getByRole('button', { name: 'Move up Note' }))
      await userEvent.click(lines.getByRole('button', { name: 'Apply' }))

      expect(headers()).toEqual(['Name', 'Note', 'Amount'])
      await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
      await userEvent.click(within(columnsDialog()).getByRole('checkbox', { name: 'Amount' }))
      await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Apply' }))

      expect(headers()).toEqual(['Name', 'Note'])
      await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
      expect(within(columnsDialog()).getByRole('checkbox', { name: 'Name' })).toBeDisabled()
      expect(within(columnsDialog()).getByRole('checkbox', { name: 'Amount' })).not.toBeChecked()
    })

    it('moves a column down', async () => {
      render(<Harness />)

      await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
      await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Move down Amount' }))
      await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Apply' }))

      expect(headers()).toEqual(['Name', 'Note', 'Amount'])
    })

    it('puts a column that is shown again last', async () => {
      render(<Harness initial={{ ...first, columns: ['name', 'note'] }} />)

      await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
      await userEvent.click(within(columnsDialog()).getByRole('checkbox', { name: 'Amount' }))
      await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Apply' }))

      expect(headers()).toEqual(['Name', 'Note', 'Amount'])
    })

    it('throws the changes away on Cancel', async () => {
      const onQuery = vi.fn()
      render(<Harness onQuery={onQuery} />)

      await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
      await userEvent.click(within(columnsDialog()).getByRole('checkbox', { name: 'Amount' }))
      await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Cancel' }))

      expect(onQuery).not.toHaveBeenCalled()
      expect(headers()).toEqual(['Name', 'Amount', 'Note'])
    })

    it('still finds a row by the text of a hidden column', async () => {
      render(<Harness initial={{ ...first, columns: ['name', 'amount'] }} />)
      expect(headers()).toEqual(['Name', 'Amount'])

      await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'later{Enter}')

      expect(screen.getByRole('row', { name: /Fix door/ })).toBeInTheDocument()
      expect(screen.queryByRole('row', { name: /Buy paper/ })).not.toBeInTheDocument()
    })

    it('still offers a hidden column in the Filter dialog', async () => {
      render(<Harness initial={{ ...first, columns: ['name', 'amount'] }} />)

      await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
      await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Column' }))

      expect(screen.getByRole('option', { name: 'Note' })).toBeInTheDocument()
    })

    it('brings every column back in its first order on Reset and Apply, and clears the columns of the view', async () => {
      const onQuery = vi.fn()
      render(<Harness initial={{ ...first, columns: ['name', 'note'] }} onQuery={onQuery} />)
      expect(headers()).toEqual(['Name', 'Note'])

      await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
      await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Reset' }))
      await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Apply' }))

      expect(headers()).toEqual(['Name', 'Amount', 'Note'])
      expect(onQuery.mock.lastCall![0].columns).toBeUndefined()
    })

    it('keeps the page when the columns change', async () => {
      const onQuery = vi.fn()
      render(<Harness initial={{ ...first, page: 1, pageSize: 10 }} onQuery={onQuery} />)

      await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
      await userEvent.click(within(columnsDialog()).getByRole('checkbox', { name: 'Amount' }))
      await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Apply' }))

      expect(onQuery).toHaveBeenCalledWith({ page: 1, pageSize: 10, sort: null, columns: ['name', 'note'] })
    })

    it('draws the identifying column first even when the view lists it later', () => {
      render(<Harness initial={{ ...first, columns: ['amount', 'name'] }} />)

      expect(headers()).toEqual(['Name', 'Amount'])
    })

    it('has no Columns button unless the view turns it on', () => {
      renderTable({ view: { query: first, onChange: () => {} } })

      expect(screen.queryByRole('button', { name: 'Columns' })).not.toBeInTheDocument()
    })
  })
})
