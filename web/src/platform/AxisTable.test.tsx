import { DeleteOutlined } from '@ant-design/icons'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Button } from 'antd'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AxisTable, type AxisColumn, type AxisRowAction, type AxisTableProps } from './AxisTable'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import { narrowWidthQuery } from './useNarrowWidth'

interface Row {
  id: string
  name: string | null
  amount: number | null
  group?: boolean
}

const texts = {
  'shell.table.refresh': 'Refresh',
  'shell.table.rowActions': 'Actions',
  'row.delete': 'Delete',
  'row.open': 'Open',
  'row.close': 'Close',
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

      act(() => screen.getByRole('button', { name: 'Actions' }).focus())

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
})
