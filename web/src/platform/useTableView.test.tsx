import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import { badgeCount } from '../test/antd'
import { clickInDialog, dialog, filterTexts, pick } from '../test/tableFilter'
import { AxisTable, type AxisColumn } from './AxisTable'
import { TextProvider } from './TextProvider'
import { useTableView } from './useTableView'

interface Row {
  id: string
  name: string
  amount: number | null
  status: 'open' | 'done'
}

const texts = {
  'shell.table.search': 'Search',
  'shell.table.range': '{from}–{to} of {total}',
  ...filterTexts,
  'shell.form.cancel': 'Cancel',
}
const catalogs = [{ texts, fallbackTexts: texts }]
const fields = [
  { name: 'name', type: 'text' },
  { name: 'amount', type: 'integer' },
  { name: 'status', type: 'enum' },
] as const
const statuses = [
  { value: 'open', label: 'Open' },
  { value: 'done', label: 'Done' },
]
const columns: AxisColumn<Row>[] = [
  {
    key: 'name',
    title: 'Name',
    dataIndex: 'name',
    kind: 'freeText',
    searchText: (row) => row.name,
    sortValue: (row) => row.name,
    filter: { kind: 'text', value: (row) => row.name },
  },
  {
    key: 'amount',
    title: 'Amount',
    dataIndex: 'amount',
    kind: 'fixed',
    filter: { kind: 'number', value: (row) => row.amount },
  },
  {
    key: 'status',
    title: 'Status',
    dataIndex: 'status',
    kind: 'fixed',
    filter: { kind: 'enum', value: (row) => row.status, options: statuses },
  },
]

function Harness({ rows }: { rows: Row[] }) {
  const { view, setView } = useTableView(fields, 20, [], true, true)
  return (
    <AxisTable<Row>
      columns={columns}
      rowKey={(row) => row.id}
      dataSource={rows}
      view={{ query: view, onChange: setView, pageSizes: [10, 20] }}
    />
  )
}

function Address() {
  return <output data-testid="address">{useLocation().search}</output>
}

function mount(address = '/', rows: Row[] = notes) {
  return render(
    <MemoryRouter initialEntries={[address]}>
      <TextProvider catalogs={catalogs} development={false}>
        <Harness rows={rows} />
      </TextProvider>
      <Address />
    </MemoryRouter>,
  )
}

const notes: Row[] = [
  { id: '1', name: 'Pear', amount: 12, status: 'open' },
  { id: '2', name: 'Apple', amount: null, status: 'done' },
  { id: '3', name: 'Papaya', amount: 3, status: 'open' },
]

const parcels: Row[] = [
  { id: '1', name: 'Pear', amount: 12, status: 'open' },
  { id: '2', name: 'Papaya', amount: 3, status: 'open' },
  { id: '3', name: 'Paste', amount: 20, status: 'done' },
  { id: '4', name: 'Parsley', amount: null, status: 'open' },
  { id: '5', name: 'Panel', amount: 10, status: 'done' },
]

const names = () =>
  screen
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getAllByRole('cell')[0].textContent)
const address = () => screen.getByTestId('address').textContent
const filterInAddress = () => JSON.parse(new URLSearchParams(address()!).get('filter')!) as unknown

describe('useTableView', () => {
  it('sorts from a header and searches the rows, keeps both in the address and shows the same rows again from it', async () => {
    const first = mount()
    expect(names()).toEqual(['Pear', 'Apple', 'Papaya'])

    await userEvent.click(screen.getByRole('columnheader', { name: 'Name' }))
    expect(names()).toEqual(['Apple', 'Papaya', 'Pear'])
    expect(address()).toBe('?sort=name')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'pa{Enter}')
    expect(names()).toEqual(['Papaya'])
    expect(address()).toBe('?search=pa&sort=name')
    first.unmount()

    mount('/?search=pa&sort=name')
    expect(names()).toEqual(['Papaya'])
    expect(screen.getByRole('searchbox', { name: 'Search' })).toHaveValue('pa')
  })

  it('sorts a header again in descending order', async () => {
    mount()

    await userEvent.click(screen.getByRole('columnheader', { name: 'Name' }))
    await userEvent.click(screen.getByRole('columnheader', { name: 'Name' }))
    expect(names()).toEqual(['Pear', 'Papaya', 'Apple'])
    expect(address()).toBe('?sort=-name')
  })

  it('goes back to page 1 on a new search', async () => {
    const many = Array.from({ length: 25 }, (_, index) => ({
      id: String(index),
      name: `Row ${index}`,
      amount: null,
      status: 'open' as const,
    }))
    mount('/?page=2', many)
    expect(names()).toHaveLength(5)
    expect(screen.getByText('21–25 of 25')).toBeInTheDocument()

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'Row 1{Enter}')
    expect(address()).toBe('?search=Row+1')
    expect(screen.getByText('1–11 of 11')).toBeInTheDocument()
  })

  it('applies a search on Enter, not on each key', async () => {
    mount()

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search' }), 'pa')
    expect(names()).toHaveLength(3)
    expect(address()).toBe('')
  })

  it('applies a nested filter, keeps it in the address and shows the same rows again from it', async () => {
    const first = mount('/', parcels)
    expect(names()).toHaveLength(5)

    await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
    await pick('Operator', 0, 'contains')
    await userEvent.type(within(dialog()).getByRole('textbox', { name: 'Value' }), 'pa')
    await clickInDialog('Add group')
    // The root group's switch comes first, so the nested group's is the last.
    await userEvent.click(screen.getAllByText('Any', { selector: '.ant-segmented-item-label' }).at(-1)!)
    await pick('Column', 1, 'Amount')
    await pick('Operator', 1, 'is more than')
    await userEvent.type(within(dialog()).getByRole('spinbutton', { name: 'Value' }), '10')
    // The nested group's buttons come before the root group's.
    await clickInDialog('Add condition', 0)
    await pick('Column', 2, 'Amount')
    await pick('Operator', 2, 'is empty')
    await userEvent.click(within(dialog()).getByRole('button', { name: 'Apply' }))

    await waitFor(() => expect(names()).toEqual(['Paste', 'Parsley']))
    const filter = {
      all: [
        { field: 'name', op: 'contains', value: 'pa' },
        {
          any: [
            { field: 'amount', op: 'gt', value: 10 },
            { field: 'amount', op: 'empty' },
          ],
        },
      ],
    }
    expect(filterInAddress()).toEqual(filter)
    first.unmount()

    mount(`/?${new URLSearchParams({ filter: JSON.stringify(filter) })}`, parcels)
    expect(names()).toEqual(['Paste', 'Parsley'])
    expect(screen.getByText('Name contains pa')).toBeInTheDocument()
    expect(screen.getByText('Any of 2 conditions')).toBeInTheDocument()
  })

  it('counts the conditions in force on the Filter button and leaves out a condition with no value', async () => {
    mount('/', parcels)
    expect(screen.getByRole('button', { name: 'Filter' })).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
    await pick('Operator', 0, 'contains')
    await userEvent.type(within(dialog()).getByRole('textbox', { name: 'Value' }), 'pa')
    await clickInDialog('Add condition')
    await userEvent.click(within(dialog()).getByRole('button', { name: 'Apply' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Filter 1' })).toBeInTheDocument())
    expect(badgeCount(screen.getByRole('button', { name: 'Filter 1' }))).toBe('1')
    expect(filterInAddress()).toEqual({ all: [{ field: 'name', op: 'contains', value: 'pa' }] })
    expect(screen.getAllByText(/^Name /)).toHaveLength(1)
  })

  it('removes every chip and the filter from the address on Clear all', async () => {
    const filter = {
      all: [
        { field: 'name', op: 'contains', value: 'pa' },
        {
          any: [
            { field: 'amount', op: 'gt', value: 10 },
            { field: 'status', op: 'eq', value: 'open' },
          ],
        },
      ],
    }
    mount(`/?${new URLSearchParams({ filter: JSON.stringify(filter) })}`, parcels)
    expect(screen.getByText('Name contains pa')).toBeInTheDocument()
    expect(screen.getByText('Any of 2 conditions')).toBeInTheDocument()
    expect(names()).toEqual(['Papaya', 'Paste', 'Parsley'])

    await userEvent.click(screen.getByRole('button', { name: 'Clear all' }))

    expect(screen.queryByText('Name contains pa')).not.toBeInTheDocument()
    expect(screen.queryByText('Any of 2 conditions')).not.toBeInTheDocument()
    expect(address()).toBe('')
    expect(names()).toHaveLength(5)
  })

  it('removes one chip with its close button', async () => {
    const filter = {
      all: [
        { field: 'name', op: 'contains', value: 'pa' },
        { field: 'status', op: 'eq', value: 'done' },
      ],
    }
    mount(`/?${new URLSearchParams({ filter: JSON.stringify(filter) })}`, parcels)
    expect(names()).toEqual(['Paste', 'Panel'])

    await userEvent.click(screen.getAllByRole('button', { name: 'Remove' })[0])

    expect(screen.queryByText('Name contains pa')).not.toBeInTheDocument()
    expect(screen.getByText('Status is Done')).toBeInTheDocument()
    expect(filterInAddress()).toEqual({ all: [{ field: 'status', op: 'eq', value: 'done' }] })
  })
})
