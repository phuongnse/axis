import { DeleteOutlined } from '@ant-design/icons'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Button } from 'antd'
import { useState, type HTMLAttributes } from 'react'
import { MemoryRouter, useLocation, useNavigationType } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { chooseOption, optionTexts, tabTo } from '../test/antd'
import { withAntdStyles } from '../test/antdStyles'
import { dialog, filterTexts } from '../test/tableFilter'
import { AxisTable, type AxisColumn, type AxisRowAction, type AxisTableProps, type AxisTableToolbar } from './AxisTable'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import type { TableQuery } from './tableQuery'
import { narrowWidthQuery } from './useNarrowWidth'
import { useTableView } from './useTableView'

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
  'shell.actionRow.more': 'More actions',
  'shell.table.selected': '{count} selected',
  'shell.table.range': '{from}–{to} of {total}',
  'shell.table.search': 'Search',
  'shell.table.columns': 'Columns',
  'shell.table.columnsApply': 'Apply',
  'shell.table.columnsReset': 'Reset',
  'shell.table.columnsMoveUp': 'Move up',
  'shell.table.columnsMoveDown': 'Move down',
  'shell.form.cancel': 'Cancel',
  'shell.form.save': 'Save',
  'shell.table.views': 'Views',
  'shell.table.viewsEmpty': 'No views yet.',
  'shell.table.viewsName': 'Name',
  'shell.table.viewsName.help': 'The name the view is listed under.',
  'shell.table.viewsNameRequired': 'Enter a name.',
  'shell.table.viewsNameTaken': 'Another view has this name.',
  'shell.table.viewsSave': 'Save view',
  'shell.table.viewsSaveFailed': 'The view could not be saved.',
  'shell.table.viewsRename': 'Rename',
  'shell.table.viewsRenameFailed': 'The view could not be renamed.',
  'shell.table.viewsOverwrite': 'Overwrite',
  'shell.table.viewsOverwriteConfirm': 'Overwrite the view {name} with the current search, filter and sort?',
  'shell.table.viewsOverwriteFailed': 'The view could not be overwritten.',
  'shell.table.viewsDelete': 'Delete',
  'shell.table.viewsDeleteConfirm': 'Delete the view {name}?',
  'shell.table.viewsDeleteFailed': 'The view could not be deleted.',
  'shell.table.selectRow': 'Select row',
  'shell.table.showDetails': 'Show the details',
  'shell.table.hideDetails': 'Hide the details',
  'shell.table.showRows': 'Show the rows in it',
  'shell.table.hideRows': 'Hide the rows in it',
  'row.archive': 'Archive',
  'row.delete': 'Delete',
  'row.open': 'Open',
  'row.close': 'Close',
  'row.revoke': 'Revoke',
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

    it('shares the width between free-text columns in equal parts and wraps their text at the column edge', () => {
      renderTable({ columns: [nameColumn, noteColumn, amountColumn] })

      expect(screen.getByRole('columnheader', { name: 'Name' })).toHaveStyle({ width: '50%' })
      expect(screen.getByRole('columnheader', { name: 'Note' })).toHaveStyle({ width: '50%' })
      const cell = screen.getAllByRole('cell', { name: 'Buy paper' })[0]
      const content = cell.firstElementChild as HTMLElement
      expect(content).toHaveStyle({ whiteSpace: 'normal', overflowWrap: 'anywhere' })
      expect(content.style.maxWidth).toBe('')
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

  describe('details', () => {
    const detailed = [
      { id: '1', name: 'Buy paper', amount: 12 },
      { id: '2', name: 'Buy ink', amount: 8 },
      { id: '3', name: 'Nothing more', amount: 1 },
    ]
    const details = (row: Row) => (row.id === '3' ? null : <span>{`More about ${row.name}`}</span>)

    it("opens and closes a row's details from the fold in its first cell, and leaves the other rows closed", async () => {
      renderTable({ dataSource: detailed, details })

      expect(screen.queryByText('More about Buy paper')).not.toBeInTheDocument()
      const [first] = screen.getAllByRole('button', { name: 'Show the details' })
      expect(first).toHaveAttribute('aria-expanded', 'false')
      expect(within(first.closest('td')!).getByText('Buy paper')).toBeVisible()

      await userEvent.click(first)

      expect(screen.getByText('More about Buy paper')).toBeVisible()
      expect(screen.queryByText('More about Buy ink')).not.toBeInTheDocument()
      const open = screen.getByRole('button', { name: 'Hide the details' })
      expect(open).toHaveAttribute('aria-expanded', 'true')
      expect(screen.getAllByRole('button', { name: 'Show the details' })).toHaveLength(1)

      await userEvent.click(open)

      expect(screen.queryByText('More about Buy paper')).not.toBeVisible()
      expect(screen.getAllByRole('button', { name: 'Show the details' })).toHaveLength(2)
    })

    it('draws no fold, and no column for it, in a row whose details are null', () => {
      renderTable({ dataSource: detailed, details })

      expect(screen.getAllByRole('button', { name: 'Show the details' })).toHaveLength(2)
      expect(screen.getAllByRole('columnheader')).toHaveLength(2)
      const row = screen.getByRole('cell', { name: 'Nothing more' }).closest('tr')!
      expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    })
  })

  describe('group rows', () => {
    const inside: Record<string, Row[]> = {
      '1': [
        { id: '1/a', name: 'Sheets', amount: 3 },
        { id: '1/b', name: 'Envelopes', amount: 9 },
      ],
      '1/a': [{ id: '1/a/x', name: 'A4', amount: 1 }],
      '2': [{ id: '2/a', name: 'Cartridges', amount: 2 }],
    }
    const topLevel: Row[] = [
      { id: '1', name: 'Paper', amount: 12 },
      { id: '2', name: 'Ink', amount: 8 },
      { id: '3', name: 'Nothing inside', amount: 1 },
    ]
    const rowsIn = (row: Row) => inside[row.id] ?? null

    it('starts closed, opens its rows from a fold named "Show the rows in it" and closes them from "Hide the rows in it"', async () => {
      renderTable({ dataSource: topLevel, rowsIn })

      expect(screen.queryByText('Sheets')).not.toBeInTheDocument()
      const [first] = screen.getAllByRole('button', { name: 'Show the rows in it' })
      expect(first).toHaveAttribute('aria-expanded', 'false')
      expect(within(first.closest('td')!).getByText('Paper')).toBeVisible()

      await userEvent.click(first)

      expect(screen.getByText('Sheets')).toBeVisible()
      expect(screen.getByText('Envelopes')).toBeVisible()
      expect(screen.queryByText('Cartridges')).not.toBeInTheDocument()
      const open = screen.getByRole('button', { name: 'Hide the rows in it' })
      expect(open).toHaveAttribute('aria-expanded', 'true')

      await userEvent.click(open)

      expect(screen.queryByText('Sheets')).not.toBeInTheDocument()
      expect(screen.getAllByRole('button', { name: 'Show the rows in it' })).toHaveLength(2)
    })

    it('draws no fold in a row with no rows in it', () => {
      renderTable({ dataSource: topLevel, rowsIn })

      const row = screen.getByRole('cell', { name: 'Nothing inside' }).closest('tr')!
      expect(within(row).queryByRole('button')).not.toBeInTheDocument()
    })

    it('opens a nested level on its own and starts each level one fold further in', async () => {
      renderTable({ dataSource: topLevel, rowsIn })
      const start = (name: string) => parseFloat(screen.getByRole('cell', { name }).style.paddingInlineStart)

      await userEvent.click(screen.getAllByRole('button', { name: 'Show the rows in it' })[0])
      expect(screen.queryByText('A4')).not.toBeInTheDocument()
      await userEvent.click(
        within(screen.getByRole('cell', { name: 'Sheets' })).getByRole('button', { name: 'Show the rows in it' }),
      )

      expect(screen.getByText('A4')).toBeVisible()
      const { size, spacing } = designTokensFor('light')
      expect(start('Sheets') - start('Paper')).toBe(size.smallControl)
      expect(start('A4') - start('Sheets')).toBe(size.smallControl)
      // Every row's words start where the header's do, one fold in from the cell's edge.
      expect(screen.getByRole('columnheader', { name: 'Name' }).style.paddingInlineStart).toBe(`${start('Paper')}px`)
      expect(start('Paper')).toBe(spacing.lg + size.smallControl + spacing.xs)
    })

    it('pages the rows it holds and counts only the rows that are not inside another', async () => {
      const onChange = vi.fn()
      renderTable({
        dataSource: topLevel,
        rowsIn,
        pagination: undefined,
        view: { query: { page: 1, pageSize: 2, sort: null }, onChange, pageSizes: [2, 10] },
      })

      expect(screen.getByText('1–2 of 3')).toBeInTheDocument()
      expect(screen.queryByText('Nothing inside')).not.toBeInTheDocument()
      await userEvent.click(screen.getAllByRole('button', { name: 'Show the rows in it' })[0])
      await userEvent.click(
        within(screen.getByRole('cell', { name: 'Sheets' })).getByRole('button', { name: 'Show the rows in it' }),
      )

      // Open rows add no row to the page's count: it still holds two of the three rows.
      expect(screen.getByText('A4')).toBeVisible()
      expect(screen.getByText('Ink')).toBeVisible()
      expect(screen.getByText('1–2 of 3')).toBeInTheDocument()
      expect(screen.queryByText('Nothing inside')).not.toBeInTheDocument()

      await userEvent.click(screen.getByRole('listitem', { name: '2' }).querySelector('a, button') as HTMLElement)

      expect(onChange).toHaveBeenCalledWith({ page: 2, pageSize: 2, sort: null })
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

  it('makes the sideways-scrolling region of the table reachable by keyboard', () => {
    const { container } = renderTable()

    expect(container.querySelector('.ant-table-content')).toHaveAttribute('tabindex', '0')
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

    it('draws a text action as a subtle button, and a destructive one in dangerText with no border', () => {
      const revokeAction: AxisRowAction = { key: 'revoke', labelKey: 'row.revoke', destructive: true, onClick: vi.fn() }
      renderTable({ rowActions: actionsFor(openAction, revokeAction) })

      const row = screen.getByRole('row', { name: /Buy paper/ })
      const open = within(row).getByRole('button', { name: 'Open' })
      expect(open).toHaveClass('ant-btn-variant-text')
      expect(open.style.color).toBe('')
      const revoke = within(row).getByRole('button', { name: 'Revoke' })
      expect(revoke).toHaveClass('ant-btn-variant-text')
      expect(revoke).toHaveStyle({ color: designTokensFor('light').colour.dangerText })
      expect(revoke.style.borderColor).toBe('')
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

    it('puts Search and Filter on the left, then Refresh, the secondary actions and the main action last', () => {
      const searchable: AxisColumn<Row> = {
        ...nameColumn,
        searchText: (row) => row.name ?? '',
        filter: { kind: 'text' },
      }
      renderTable({
        columns: [searchable, amountColumn],
        view: { query: { page: 1, pageSize: 20, sort: null }, onChange: vi.fn() },
        pagination: undefined,
        toolbar: {
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

      const search = screen.getByRole('searchbox', { name: 'Search' })
      const filter = screen.getByRole('button', { name: 'Filter' })
      const refresh = screen.getByRole('button', { name: 'Refresh' })
      const right = refresh.parentElement!
      expect(
        within(right)
          .getAllByRole('button')
          .map((button) => button.textContent || button.getAttribute('aria-label')),
      ).toEqual(['Refresh', 'Import', 'Export', 'Add'])
      expect(within(right).queryByRole('searchbox')).not.toBeInTheDocument()
      expect(within(right).queryByRole('button', { name: 'Filter' })).not.toBeInTheDocument()
      expect(search.compareDocumentPosition(filter) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(filter.compareDocumentPosition(refresh) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    })

    describe('search box width', () => {
      withAntdStyles()

      it('keeps the search box at its own width so Filter and Columns follow it on one line, and wrap when there is no room', () => {
        const searchable: AxisColumn<Row> = {
          ...nameColumn,
          searchText: (row) => row.name ?? '',
          filter: { kind: 'text' },
        }
        renderTable({
          columns: [searchable, amountColumn],
          view: { query: { page: 1, pageSize: 20, sort: null }, onChange: vi.fn(), columns: true },
          pagination: undefined,
        })

        const search = screen.getByRole('searchbox', { name: 'Search' }).closest('.ant-input-search') as HTMLElement
        expect(search).toHaveStyle({ width: '320px', maxWidth: '100%' })
        // Filter and Columns sit in the same wrapping container as the search box, so they wrap with it.
        const controls = search.parentElement as HTMLElement
        expect(within(controls).getByRole('button', { name: 'Filter' })).toBeInTheDocument()
        expect(within(controls).getByRole('button', { name: 'Columns' })).toBeInTheDocument()
        expect(controls).toHaveStyle({ flexWrap: 'wrap', minWidth: '0px' })
      })
    })

    it('has no slot for a control of its own', () => {
      // @ts-expect-error A table has no control of its own for a view, see docs/reference/actions-and-controls.md#tables.
      const toolbar: AxisTableToolbar = { view: <input aria-label="Search" /> }
      expect(toolbar).toBeDefined()
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

  it('offers a column whose header is not plain text under its filter title', async () => {
    renderTable({
      columns: [
        {
          ...nameColumn,
          title: (
            <div>
              <span>Name</span>
              <small>text</small>
            </div>
          ),
          filter: { kind: 'text', title: 'Due', value: (row) => row.name },
        },
        amountColumn,
      ],
      view: { query: { page: 1, pageSize: 20, sort: null }, onChange: vi.fn() },
    })

    await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
    await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Column' }))

    expect(optionTexts()).toEqual(['Due'])
  })

  describe('a view with paging off', () => {
    const many = Array.from({ length: 25 }, (_, index) => ({ id: String(index), name: `Row ${index}`, amount: index }))
    const bodyRows = () => screen.getAllByRole('row').filter((row) => within(row).queryAllByRole('cell').length > 0)
    const filterable: AxisColumn<Row> = {
      ...nameColumn,
      filter: { kind: 'text', value: (row) => row.name },
    }

    it('shows every row with no pager even when the rows run past the page size', () => {
      renderTable({
        columns: [filterable, amountColumn],
        dataSource: many,
        view: { query: { page: 1, pageSize: 20, sort: null }, onChange: vi.fn(), paging: false },
      })

      expect(bodyRows()).toHaveLength(25)
      expect(screen.queryByRole('list')).not.toBeInTheDocument()
      expect(screen.queryByText(/of 25/)).not.toBeInTheDocument()
    })

    it('shows a kept row that the filter would hide, and still hides the others', () => {
      renderTable({
        columns: [filterable, amountColumn],
        dataSource: many,
        view: {
          query: {
            page: 1,
            pageSize: 20,
            sort: null,
            filter: { all: [{ field: 'name', op: 'eq', value: 'no such row' }] },
          },
          onChange: vi.fn(),
          paging: false,
        },
        keep: (row) => row.id === '24',
      })

      expect(bodyRows()).toHaveLength(1)
      expect(screen.getByRole('row', { name: /Row 24/ })).toBeInTheDocument()
    })
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

    describe('kept columns', () => {
      const keptFields = [
        { name: 'name', type: 'text' },
        { name: 'amount', type: 'integer' },
        { name: 'note', type: 'text' },
      ] as const
      const tableUrl = '/api/me/tables/test.table'

      /** A server that keeps the columns of one table. `read` is called when the answer to a read is taken. */
      function serveChoices(kept: string[] | null, readStatus = 200) {
        const server = { kept, read: vi.fn() }
        const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
          const method = init?.method ?? 'GET'
          if (url === tableUrl && method === 'GET') {
            const response = new Response(null, { status: readStatus })
            vi.spyOn(response, 'json').mockImplementation(async () => {
              server.read()
              return { columns: server.kept, views: [] }
            })
            return response
          }
          if (url === `${tableUrl}/columns` && method === 'PUT') {
            server.kept = (JSON.parse(String(init?.body)) as { columns: string[] }).columns
          } else if (url === `${tableUrl}/columns` && method === 'DELETE') {
            server.kept = null
          }
          return new Response(null, { status: 204 })
        })
        vi.stubGlobal('fetch', fetchMock)
        return { server, fetchMock }
      }

      function KeptHarness() {
        const { view, setView } = useTableView(keptFields, 20, [], true, true, true, '', 'test.table')
        return (
          <TextProvider catalogs={catalogs} development={false}>
            <AxisTable<Row>
              columns={arranged}
              rowKey={(row) => row.id}
              dataSource={noted}
              view={{ query: view, onChange: setView, columns: true }}
            />
          </TextProvider>
        )
      }

      function Probe() {
        return (
          <>
            <output data-testid="address">{useLocation().search}</output>
            <output data-testid="navigation">{useNavigationType()}</output>
          </>
        )
      }

      const open = (address = '/') =>
        render(
          <MemoryRouter initialEntries={[address]}>
            <KeptHarness />
            <Probe />
          </MemoryRouter>,
        )

      const applyColumns = async (change: () => Promise<void>) => {
        await userEvent.click(screen.getByRole('button', { name: 'Columns' }))
        await change()
        await userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Apply' }))
      }

      const mutations = (fetchMock: ReturnType<typeof serveChoices>['fetchMock']) =>
        fetchMock.mock.calls.filter(([, init]) => init?.method !== undefined)

      it('keeps the columns that Apply sets, and shows them again when the table opens with no columns', async () => {
        const { server, fetchMock } = serveChoices(null)
        const first = open()
        await waitFor(() => expect(server.read).toHaveBeenCalled())

        await applyColumns(() => userEvent.click(within(columnsDialog()).getByRole('checkbox', { name: 'Amount' })))

        await waitFor(() => expect(server.kept).toEqual(['name', 'note']))
        const [url, init] = mutations(fetchMock)[0]
        expect(url).toBe(`${tableUrl}/columns`)
        expect(init?.method).toBe('PUT')
        expect(init?.body).toBe('{"columns":["name","note"]}')
        expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
        first.unmount()

        open()

        await waitFor(() => expect(headers()).toEqual(['Name', 'Note']))
        expect(screen.getByTestId('address')).toHaveTextContent('columns=name%2Cnote')
        expect(screen.getByTestId('navigation')).toHaveTextContent('REPLACE')
      })

      it('shows the columns of the address, not the kept ones, and forgets the choice on Reset and Apply', async () => {
        const { server, fetchMock } = serveChoices(['name', 'note'])
        open('/?columns=name,amount')
        await waitFor(() => expect(server.read).toHaveBeenCalled())
        await act(async () => {})

        expect(headers()).toEqual(['Name', 'Amount'])
        expect(mutations(fetchMock)).toHaveLength(0)

        await applyColumns(() => userEvent.click(within(columnsDialog()).getByRole('button', { name: 'Reset' })))

        await waitFor(() => expect(server.kept).toBeNull())
        const [url, init] = mutations(fetchMock)[0]
        expect(url).toBe(`${tableUrl}/columns`)
        expect(init?.method).toBe('DELETE')
        expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
        expect(headers()).toEqual(['Name', 'Amount', 'Note'])
      })

      it('shows every column and leaves the address alone when the choices are refused', async () => {
        const { fetchMock } = serveChoices(['name', 'note'], 401)
        open()
        await waitFor(() => expect(fetchMock).toHaveBeenCalledWith(tableUrl, expect.anything()))
        await act(async () => {})

        expect(headers()).toEqual(['Name', 'Amount', 'Note'])
        expect(screen.getByTestId('address')).toBeEmptyDOMElement()
        expect(screen.getByTestId('navigation')).toHaveTextContent('POP')
      })
    })

    describe('saved views', () => {
      const keptFields = [
        { name: 'name', type: 'text' },
        { name: 'amount', type: 'integer' },
        { name: 'note', type: 'text' },
      ] as const
      const tableUrl = '/api/me/tables/test.table'
      const filter = JSON.stringify({ all: [{ field: 'note', op: 'contains', value: 'urgent' }] })

      interface Stored {
        id: string
        name: string
        search: string | null
        filter: string | null
        sort: string | null
      }

      /** A server that keeps the views of one table. `saveStatus` is what a POST answers and `replaceStatus` what a PUT answers. */
      function serveViews(views: Stored[], saveStatus = 201) {
        const server = { views, saveStatus, replaceStatus: 204 }
        const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
          const method = init?.method ?? 'GET'
          if (url === tableUrl && method === 'GET') {
            return Response.json({ columns: null, views: server.views })
          }
          if (url === `${tableUrl}/views` && method === 'POST') {
            if (server.saveStatus !== 201) {
              return new Response(null, { status: server.saveStatus })
            }
            const saved = { id: 'new', ...(JSON.parse(String(init?.body)) as Omit<Stored, 'id'>) }
            server.views = [...server.views, saved]
            return Response.json(saved, { status: 201 })
          }
          if (url.startsWith(`${tableUrl}/views/`) && method === 'PUT') {
            return new Response(null, { status: server.replaceStatus })
          }
          return new Response(null, { status: 204 })
        })
        vi.stubGlobal('fetch', fetchMock)
        return { server, fetchMock }
      }

      function ViewsHarness() {
        const { view, setView, saved } = useTableView(keptFields, 20, [], true, true, false, '', 'test.table')
        return (
          <TextProvider catalogs={catalogs} development={false}>
            <AxisTable<Row>
              columns={arranged}
              rowKey={(row) => row.id}
              dataSource={noted}
              view={{ query: view, onChange: setView, saved }}
            />
          </TextProvider>
        )
      }

      function Probe() {
        return (
          <>
            <output data-testid="address">{useLocation().search}</output>
            <output data-testid="navigation">{useNavigationType()}</output>
          </>
        )
      }

      const open = (address = '/') =>
        render(
          <MemoryRouter initialEntries={[address]}>
            <ViewsHarness />
            <Probe />
          </MemoryRouter>,
        )

      const viewsDialog = () => screen.getByRole('dialog', { name: 'Views' })
      const openViews = async () => {
        await userEvent.click(await screen.findByRole('button', { name: 'Views' }))
        return viewsDialog()
      }
      /** Opens the caret's menu of a view's line and chooses an entry. The menu is in a portal, outside the dialog. */
      const chooseFromMenu = async (dialog: HTMLElement, view: string, entry: 'Rename' | 'Delete') => {
        await userEvent.click(within(dialog).getByRole('button', { name: `More actions ${view}` }))
        await userEvent.click(await screen.findByRole('menuitem', { name: entry }))
      }
      const mutations = (fetchMock: ReturnType<typeof serveViews>['fetchMock']) =>
        fetchMock.mock.calls.filter(([, init]) => init?.method !== undefined)
      const address = () => new URLSearchParams(screen.getByTestId('address').textContent ?? '')

      it('opens the saved items dialog, whose lines are each one split button with Rename and Delete in its menu', async () => {
        serveViews([
          { id: 'a', name: 'Zed', search: null, filter: null, sort: null },
          { id: 'b', name: 'Abe', search: null, filter: null, sort: null },
        ])
        open()

        const dialog = await openViews()

        expect(dialog.querySelector('[data-saved-items]')).not.toBeNull()
        expect(dialog.querySelectorAll('.ant-space-compact')).toHaveLength(2)
        expect(
          within(dialog)
            .getAllByRole('button', { name: /^(Overwrite|More actions) / })
            .map((button) => button.getAttribute('aria-label')),
        ).toEqual(['Overwrite Abe', 'More actions Abe', 'Overwrite Zed', 'More actions Zed'])
        expect(within(dialog).queryByRole('button', { name: /^(Rename|Delete) / })).not.toBeInTheDocument()

        await userEvent.click(within(dialog).getByRole('button', { name: 'More actions Abe' }))
        const menu = await screen.findByRole('menu', { name: 'More actions' })
        const entries = within(menu).getAllByRole('menuitem')
        expect(entries.map((entry) => entry.textContent)).toEqual(['Rename', 'Delete'])
        expect(entries[0]).not.toHaveClass('ant-dropdown-menu-item-danger')
        expect(entries[1]).toHaveClass('ant-dropdown-menu-item-danger')
      })

      it('saves the address as a view, and opens it again at page 1 with a history entry', async () => {
        const { fetchMock } = serveViews([])
        const first = open(`/?search=pen&filter=${encodeURIComponent(filter)}&sort=-amount&page=2`)

        const dialog = await openViews()
        await userEvent.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'Mine')
        await userEvent.click(within(dialog).getByRole('button', { name: 'Save view' }))

        await waitFor(() => expect(mutations(fetchMock)).toHaveLength(1))
        const [url, init] = mutations(fetchMock)[0]
        expect(url).toBe(`${tableUrl}/views`)
        expect(init?.method).toBe('POST')
        expect(JSON.parse(String(init?.body))).toEqual({ name: 'Mine', search: 'pen', filter, sort: '-amount' })
        expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
        first.unmount()

        open('/?page=3&pageSize=50')
        await userEvent.click(within(await openViews()).getByRole('button', { name: 'Mine' }))

        await waitFor(() => expect(address().get('search')).toBe('pen'))
        expect(address().get('filter')).toBe(filter)
        expect(address().get('sort')).toBe('-amount')
        expect(address().has('page')).toBe(false)
        expect(address().get('pageSize')).toBe('50')
        expect(screen.getByTestId('navigation')).toHaveTextContent('PUSH')
      })

      it('clears the parts that a view leaves empty', async () => {
        serveViews([{ id: 'a', name: 'Plain', search: null, filter: null, sort: null }])
        open(`/?search=pen&filter=${encodeURIComponent(filter)}&sort=amount`)

        await userEvent.click(within(await openViews()).getByRole('button', { name: 'Plain' }))

        await waitFor(() => expect(screen.getByTestId('address')).toBeEmptyDOMElement())
      })

      it('drops the filter of a view that names a field the table no longer has', async () => {
        const lost = JSON.stringify({ all: [{ field: 'gone', op: 'eq', value: 'x' }] })
        serveViews([{ id: 'a', name: 'Old', search: 'pen', filter: lost, sort: null }])
        open()

        await userEvent.click(within(await openViews()).getByRole('button', { name: 'Old' }))

        await waitFor(() => expect(address().get('search')).toBe('pen'))
        expect(address().has('filter')).toBe(false)
      })

      it('shows no Views button before the views have loaded', async () => {
        vi.stubGlobal(
          'fetch',
          vi.fn(async () => new Response(null, { status: 401 })),
        )
        open()
        await act(async () => {})

        expect(screen.queryByRole('button', { name: 'Views' })).not.toBeInTheDocument()
      })

      it('keeps the dialog open and the table as it was when the server refuses a save', async () => {
        const { server } = serveViews([{ id: 'a', name: 'Old', search: null, filter: null, sort: null }], 409)
        open('/?search=pen')

        const dialog = await openViews()
        await userEvent.type(within(dialog).getByRole('textbox', { name: 'Name' }), 'Mine')
        await userEvent.click(within(dialog).getByRole('button', { name: 'Save view' }))

        expect(await within(dialog).findByText('The view could not be saved.')).toBeInTheDocument()
        expect(viewsDialog()).toBe(dialog)
        expect(screen.getByTestId('address')).toHaveTextContent('?search=pen')
        expect(screen.getByTestId('navigation')).toHaveTextContent('POP')
        expect(within(dialog).getAllByRole('button', { name: /^(Overwrite|More actions) / })).toHaveLength(2)
        expect(server.views).toHaveLength(1)
      })

      it('refuses an empty name and a name another view has, without sending anything', async () => {
        const { fetchMock } = serveViews([{ id: 'a', name: 'Old', search: null, filter: null, sort: null }])
        open()

        const dialog = await openViews()
        await userEvent.click(within(dialog).getByRole('button', { name: 'Save view' }))
        expect(await within(dialog).findByText('Enter a name.')).toBeInTheDocument()

        await userEvent.type(within(dialog).getByRole('textbox', { name: 'Name' }), ' old ')
        await userEvent.click(within(dialog).getByRole('button', { name: 'Save view' }))
        expect(await within(dialog).findByText('Another view has this name.')).toBeInTheDocument()
        expect(mutations(fetchMock)).toHaveLength(0)
      })

      it('puts the focus on the empty name and sends nothing', async () => {
        const { fetchMock } = serveViews([{ id: 'a', name: 'Old', search: null, filter: null, sort: null }])
        open()

        const dialog = await openViews()
        await userEvent.click(within(dialog).getByRole('button', { name: 'Save view' }))

        expect(await within(dialog).findByText('Enter a name.')).toBeInTheDocument()
        expect(within(dialog).getByRole('textbox', { name: 'Name' })).toHaveFocus()
        expect(mutations(fetchMock)).toHaveLength(0)
      })

      it('renames a view in its own line', async () => {
        const { fetchMock } = serveViews([{ id: 'a b', name: 'Old', search: 'pen', filter: null, sort: '-amount' }])
        open()

        const dialog = await openViews()
        await chooseFromMenu(dialog, 'Old', 'Rename')
        const input = within(dialog).getByRole('textbox', { name: 'Name', description: '' })
        await userEvent.clear(input)
        await userEvent.type(input, 'New{Enter}')

        expect(await within(dialog).findByRole('button', { name: 'New' })).toBeInTheDocument()
        const [url, init] = mutations(fetchMock)[0]
        expect(url).toBe(`${tableUrl}/views/a%20b`)
        expect(init?.method).toBe('PUT')
        expect(JSON.parse(String(init?.body))).toEqual({ name: 'New', search: 'pen', filter: null, sort: '-amount' })
        expect(within(dialog).queryByRole('button', { name: 'Old' })).not.toBeInTheDocument()
      })

      const viewNames = (dialog: HTMLElement) =>
        within(dialog)
          .getAllByRole('button')
          .map((button) => button.textContent)
          .filter((name) => name === 'Mine' || name === 'Zed')

      it('overwrites a view in its own line with the current search, filter and sort', async () => {
        const { fetchMock } = serveViews([
          { id: 'a b', name: 'Mine', search: null, filter: null, sort: null },
          { id: 'c', name: 'Zed', search: null, filter: null, sort: null },
        ])
        open(`/?search=pen&filter=${encodeURIComponent(filter)}&sort=-amount`)

        const dialog = await openViews()
        await userEvent.click(within(dialog).getByRole('button', { name: 'Overwrite Mine' }))
        expect(
          within(dialog).getByText('Overwrite the view Mine with the current search, filter and sort?'),
        ).toBeInTheDocument()
        expect(mutations(fetchMock)).toHaveLength(0)
        await userEvent.click(within(dialog).getByRole('button', { name: 'Overwrite' }))

        expect(await within(dialog).findByRole('button', { name: 'Overwrite Mine' })).toBeInTheDocument()
        expect(mutations(fetchMock)).toHaveLength(1)
        const [url, init] = mutations(fetchMock)[0]
        expect(url).toBe(`${tableUrl}/views/a%20b`)
        expect(init?.method).toBe('PUT')
        expect(JSON.parse(String(init?.body))).toEqual({ name: 'Mine', search: 'pen', filter, sort: '-amount' })
        expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
        expect(viewsDialog()).toBe(dialog)
        expect(viewNames(dialog)).toEqual(['Mine', 'Zed'])
        expect(address().get('search')).toBe('pen')
        expect(address().get('sort')).toBe('-amount')
      })

      it('sends nothing and shows the view again when Cancel answers the overwrite question', async () => {
        const { fetchMock } = serveViews([{ id: 'a', name: 'Mine', search: null, filter: null, sort: null }])
        open('/?search=pen')

        const dialog = await openViews()
        await userEvent.click(within(dialog).getByRole('button', { name: 'Overwrite Mine' }))
        // The line's Cancel comes before the dialog's own.
        await userEvent.click(within(dialog).getAllByRole('button', { name: 'Cancel' })[0])

        expect(within(dialog).getByRole('button', { name: 'Overwrite Mine' })).toBeInTheDocument()
        expect(mutations(fetchMock)).toHaveLength(0)
      })

      it('keeps the dialog open and the list as it was when the server refuses an overwrite', async () => {
        const { server } = serveViews([
          { id: 'a', name: 'Mine', search: null, filter: null, sort: null },
          { id: 'c', name: 'Zed', search: null, filter: null, sort: null },
        ])
        server.replaceStatus = 409
        open('/?search=pen')

        const dialog = await openViews()
        await userEvent.click(within(dialog).getByRole('button', { name: 'Overwrite Mine' }))
        await userEvent.click(within(dialog).getByRole('button', { name: 'Overwrite' }))

        expect(await within(dialog).findByText('The view could not be overwritten.')).toBeInTheDocument()
        expect(viewsDialog()).toBe(dialog)
        // The line keeps asking, so the person can try again, and the list has the same views.
        expect(
          within(dialog).getByText('Overwrite the view Mine with the current search, filter and sort?'),
        ).toBeInTheDocument()
        expect(viewNames(dialog)).toEqual(['Zed'])
        await userEvent.click(within(dialog).getAllByRole('button', { name: 'Cancel' })[0])
        expect(viewNames(dialog)).toEqual(['Mine', 'Zed'])
      })

      it('deletes a view in its own line after the question, and keeps it when Cancel answers', async () => {
        const { fetchMock } = serveViews([
          { id: 'a', name: 'Old', search: null, filter: null, sort: null },
          { id: 'b', name: 'Keep', search: null, filter: null, sort: null },
        ])
        open()

        const dialog = await openViews()
        await chooseFromMenu(dialog, 'Old', 'Delete')
        expect(within(dialog).getByText('Delete the view Old?')).toBeInTheDocument()
        expect(mutations(fetchMock)).toHaveLength(0)
        // The line's Cancel comes before the dialog's own.
        await userEvent.click(within(dialog).getAllByRole('button', { name: 'Cancel' })[0])
        expect(within(dialog).getByRole('button', { name: 'Old' })).toBeInTheDocument()

        await chooseFromMenu(dialog, 'Old', 'Delete')
        await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

        await waitFor(() => expect(within(dialog).queryByRole('button', { name: 'Old' })).not.toBeInTheDocument())
        const [url, init] = mutations(fetchMock)[0]
        expect(url).toBe(`${tableUrl}/views/a`)
        expect(init?.method).toBe('DELETE')
        expect(new Headers(init?.headers).get('X-Axis-Csrf')).toBe('1')
        expect(within(dialog).getByRole('button', { name: 'Keep' })).toBeInTheDocument()
      })

      it('lists the views by name', async () => {
        serveViews([
          { id: 'a', name: 'Zed', search: null, filter: null, sort: null },
          { id: 'b', name: 'Abe', search: null, filter: null, sort: null },
        ])
        open()

        const dialog = await openViews()

        const names = within(dialog)
          .getAllByRole('button')
          .map((button) => button.textContent)
          .filter((name) => name === 'Abe' || name === 'Zed')
        expect(names).toEqual(['Abe', 'Zed'])
      })
    })
  })

  // jsdom has no layout, so the widths are faked: the fixed Amount column is 100 px wide, and a free-text column needs
  // 208 px (160 px and its padding).
  describe('too little room', () => {
    const lowAmount: AxisColumn<Row> = { ...amountColumn, priority: 'low' }
    const columns = [nameColumn, lowAmount, noteColumn]

    function fakeWidth(width: number) {
      vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(width)
      vi.spyOn(HTMLElement.prototype, 'offsetWidth', 'get').mockImplementation(function (this: HTMLElement) {
        return this.dataset.columnKey === 'amount' ? 100 : 0
      })
    }
    const headerNames = () => screen.getAllByRole('columnheader').map((header) => header.textContent)
    const movedLines = (container: HTMLElement) => [...container.querySelectorAll('[data-moved-cells]')]

    afterEach(() => {
      vi.restoreAllMocks()
    })

    it('moves a column under its row as "Title: value" and leaves out a moved cell with no value', () => {
      fakeWidth(450)
      const { container } = renderTable({ columns })

      expect(headerNames()).toEqual(['Name', 'Note'])
      const moved = movedLines(container)
      expect(moved.map((lines) => lines.textContent)).toEqual(['Amount: 12'])
      // The lines sit in the row they belong to.
      expect(moved[0].closest('tr')).toHaveTextContent('Buy paper')
    })

    it('keeps the identifying column in the header however narrow the table is', () => {
      fakeWidth(100)
      const { container } = renderTable({ columns })

      expect(headerNames()).toEqual(['Name'])
      expect(movedLines(container).map((row) => row.textContent)).toEqual(['Amount: 12Note: Buy paper'])
    })

    it('leaves the cells that stay room under their content for the moved lines', () => {
      fakeWidth(450)
      const { container } = renderTable({ columns })

      const row = container.querySelector('[data-moved-cells]')!.closest('tr')!
      const [name, note] = row.querySelectorAll('td')
      expect(note.style.paddingBottom).toContain('var(--axis-moved-height')
      // The identifying cell holds the lines itself and needs no room.
      expect(name.style.paddingBottom).toBe('')
      expect(row.style.getPropertyValue('--axis-moved-height')).toBe('0px')
    })

    it.each([
      ['has room', 1000],
      ['moves columns', 450],
    ])('keeps the row actions on the first line of the row when the table %s', (_case, width) => {
      fakeWidth(width)
      const { container } = renderTable({
        columns,
        rowActions: { configured: 1, actions: () => [deleteAction] },
      })

      const cell = container.querySelector('tbody tr:not([aria-hidden]) td:last-child') as HTMLElement
      expect(within(cell).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
      expect(cell.style.verticalAlign).toBe('top')
      expect(cell.style.whiteSpace).toBe('nowrap')
      expect(cell.style.paddingBottom).toContain('var(--axis-moved-height')
    })

    it('shows every column and no moved lines when the columns fit', () => {
      fakeWidth(1000)
      const { container } = renderTable({ columns })

      expect(headerNames()).toEqual(['Name', 'Amount', 'Note'])
      expect(movedLines(container)).toEqual([])
    })
  })

  describe('title, compact and headless', () => {
    withAntdStyles()

    it('draws its title first at the left end of the toolbar, before the search box', () => {
      const searchable: AxisColumn<Row> = { ...nameColumn, searchText: (row) => row.name ?? '' }
      renderTable({
        columns: [searchable, amountColumn],
        view: { query: { page: 1, pageSize: 20, sort: null }, onChange: vi.fn() },
        pagination: undefined,
        title: <span>Lines</span>,
      })

      const title = screen.getByText('Lines')
      const search = screen.getByRole('searchbox', { name: 'Search' })
      expect(title.compareDocumentPosition(search) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
      expect(screen.getByTestId('axis-table-title').parentElement?.firstElementChild).toBe(
        screen.getByTestId('axis-table-title'),
      )
    })

    it('draws a toolbar for a table with only a title', () => {
      renderTable({ title: <span>Lines</span> })

      expect(screen.getByText('Lines')).toBeInTheDocument()
    })

    it('draws no header row when it is headless', () => {
      const { container } = renderTable({ headless: true })

      expect(container.querySelector('thead')).toBeNull()
      expect(screen.queryByRole('columnheader')).not.toBeInTheDocument()
      expect(screen.getByRole('cell', { name: 'Buy paper' })).toBeInTheDocument()
    })

    it('pads each side of a cell by spacing-sm when it is compact and spacing-lg otherwise', () => {
      const { spacing } = designTokensFor('light')
      // Ant Design gives the padding through a CSS variable that a rule names by the classes of the table's wrapper.
      const paddings = (compact: boolean) => {
        const { container, unmount } = renderTable({ compact })
        const wrapper = container.querySelector('.ant-table-wrapper') as HTMLElement
        const css = [...document.querySelectorAll('style')].map((style) => style.textContent ?? '').join('\n')
        const found = css
          .split('}')
          .filter((rule) =>
            [...wrapper.classList].some(
              (name) => name.startsWith('css-var-') && rule.split('{')[0].includes(`.${name}.`),
            ),
          )
          .map((rule) => rule.match(/--ant-table-cell-padding-inline:\s*([^;]*);/)?.[1])
          .find((value) => value !== undefined)
        unmount()
        return found
      }
      expect(paddings(true)).toBe(`${spacing.sm}px`)
      expect(paddings(false)).toBe(`${spacing.lg}px`)
    })
  })

  describe('spacing', () => {
    withAntdStyles()

    it('draws a paged table with no bottom margin on its pager, so the table ends where the pager ends', () => {
      const manyRows: Row[] = Array.from({ length: 25 }, (_, index) => ({
        id: String(index),
        name: `Item ${index}`,
        amount: index,
      }))
      const { container } = renderTable({
        dataSource: manyRows,
        pagination: { current: 1, pageSize: 20, total: manyRows.length, pageSizeOptions: [10, 20] },
      })

      const pager = container.querySelector('.ant-pagination') as HTMLElement
      expect(pager).not.toBeNull()
      // Ant Design's pager margin is `var(--ant-margin) 0`, which jsdom does not resolve. Only a bottom margin that the
      // pager's own style sets reads as 0px, so this is strict.
      expect(getComputedStyle(pager).marginBottom).toBe('0px')
    })

    it('gives the padding around the empty text of an empty table, so the table keeps its height', () => {
      const { spacing } = designTokensFor('light')
      const { container } = renderTable({ dataSource: [], pagination: false })

      const padder = container.querySelector('.ant-empty')?.parentElement as HTMLElement
      expect(padder).toBeTruthy()
      const style = getComputedStyle(padder)
      expect(style.paddingTop).toBe(`${spacing.xl}px`)
      expect(style.paddingBottom).toBe(`${spacing.xl}px`)
      expect(style.paddingLeft).toBe(`${spacing.xs}px`)
      expect(style.paddingRight).toBe(`${spacing.xs}px`)
    })
  })
})
