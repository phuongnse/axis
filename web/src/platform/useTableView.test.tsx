import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { describe, expect, it } from 'vitest'
import { AxisTable, type AxisColumn } from './AxisTable'
import { TextProvider } from './TextProvider'
import { useTableView } from './useTableView'

interface Row {
  id: string
  name: string
}

const texts = { 'shell.table.search': 'Search', 'shell.table.range': '{from}–{to} of {total}' }
const catalogs = [{ texts, fallbackTexts: texts }]
const fields = [{ name: 'name', type: 'text' }] as const
const columns: AxisColumn<Row>[] = [
  {
    key: 'name',
    title: 'Name',
    dataIndex: 'name',
    kind: 'freeText',
    searchText: (row) => row.name,
    sortValue: (row) => row.name,
  },
]

function Harness({ rows }: { rows: Row[] }) {
  const { view, setView } = useTableView(fields, 20, [], true)
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
  { id: '1', name: 'Pear' },
  { id: '2', name: 'Apple' },
  { id: '3', name: 'Papaya' },
]

const names = () =>
  screen
    .getAllByRole('row')
    .slice(1)
    .map((row) => within(row).getByRole('cell').textContent)
const address = () => screen.getByTestId('address').textContent

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
    const many = Array.from({ length: 25 }, (_, index) => ({ id: String(index), name: `Row ${index}` }))
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
})
