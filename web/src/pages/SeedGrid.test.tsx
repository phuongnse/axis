import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { TextProvider } from '../platform/TextProvider'
import { seedProblems, type SeedField } from '../platform/seedValues'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { SeedGrid } from './SeedGrid'

const fields: SeedField[] = [
  { name: 'code', type: 'text', required: true, maxLength: 20 },
  { name: 'size', type: 'integer' },
  { name: 'price', type: 'decimal', precision: 8, scale: 2 },
  { name: 'active', type: 'boolean' },
  { name: 'status', type: 'enum', values: ['Open', 'Closed'], required: true },
  { name: 'due', type: 'date' },
  { name: 'closedAt', type: 'date-time' },
  { name: 'manager', type: 'reference', target: 'Person' } as SeedField,
  { name: 'total', type: 'decimal', expression: 'price * size' },
  { name: 'number', type: 'text', sequence: 'Numbers' },
  { name: 'owner', type: 'text', creator: true },
  { name: 'lines', type: 'child-collection' },
]

const records = [
  { id: 'c6a80cfd-75d2-4847-8cc0-f603c2af74fa', values: { code: 'FIN', size: 5, status: 'Open', active: false } },
  { id: '15d5bba1-eaf1-431b-9001-e1e3ade50979', values: { code: 'ENG', size: '5', status: 'Open' } },
]

/** The theme root of the shell: an element that carries the mode the provider chose. */
function ThemeRoot({ children }: { children: ReactNode }) {
  return <div data-theme-mode={useThemeMode().mode}>{children}</div>
}

function renderGrid(onChange = vi.fn(), data: unknown = records) {
  const columns = fields.filter(
    (field) => !field.expression && !field.sequence && !field.creator && field.type !== 'child-collection',
  )
  render(
    <ThemeModeProvider>
      <ThemeRoot>
        <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
          <SeedGrid fields={fields} records={data} problems={seedProblems(data, columns)} onChange={onChange} />
        </TextProvider>
      </ThemeRoot>
    </ThemeModeProvider>,
  )
  return onChange
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('SeedGrid in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with its headers, values and error state', () => {
    renderGrid()

    const grid = screen.getByTestId('studio-seed-grid')
    expect(grid.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(grid).toBeVisible()
    for (const name of ['Id', 'code', 'size', 'price', 'active', 'status', 'due', 'closedAt', 'manager']) {
      expect(within(grid).getByRole('columnheader', { name })).toBeVisible()
    }
    const rows = screen.getAllByTestId('studio-seed-row')
    expect(rows).toHaveLength(2)
    expect(within(rows[0]).getByRole('textbox', { name: 'code' })).toHaveValue('FIN')
    expect(within(rows[0]).getByRole('textbox', { name: 'size' })).toHaveValue('5')
    // The string "5" for an integer field shows as written and as an error.
    const bad = within(rows[1]).getByRole('textbox', { name: 'size' })
    expect(bad).toHaveValue('5')
    expect(bad).toHaveAttribute('aria-invalid', 'true')
    expect(
      within(screen.getByTestId('studio-field-/records/1/values/size')).getByTestId('studio-seed-problem'),
    ).toBeVisible()
    expect(within(rows[0]).getByRole('textbox', { name: 'size' })).toHaveAttribute('aria-invalid', 'false')
  })
})

describe('SeedGrid', () => {
  it('shows the control of each field type and no column for the fields a seed may not set', () => {
    renderGrid()
    const row = screen.getAllByTestId('studio-seed-row')[0]

    for (const name of ['code', 'size', 'price', 'due', 'closedAt', 'manager']) {
      expect(within(row).getByRole('textbox', { name })).toBeInTheDocument()
    }
    for (const name of ['active', 'status']) {
      expect(within(row).getByRole('combobox', { name })).toBeInTheDocument()
    }
    expect(within(row).getByRole('textbox', { name: 'due' })).toHaveAttribute('placeholder', 'yyyy-MM-dd')
    expect(within(row).getByRole('textbox', { name: 'closedAt' })).toHaveAttribute(
      'placeholder',
      'yyyy-MM-ddTHH:mm:ssZ',
    )
    for (const name of ['total', 'number', 'owner', 'lines']) {
      expect(screen.queryByRole('columnheader', { name })).not.toBeInTheDocument()
      expect(screen.queryByRole('textbox', { name })).not.toBeInTheDocument()
    }
  })

  it('marks a required cell that is empty', () => {
    renderGrid(vi.fn(), [{ id: 'a', values: {} }])

    const cell = screen.getByTestId('studio-field-/records/0/values/code')
    expect(within(cell).getByTestId('studio-seed-problem')).toHaveTextContent('A value is required.')
  })

  it('removes the key when a cell is cleared and sets a typed number', async () => {
    const onChange = renderGrid()
    const row = screen.getAllByTestId('studio-seed-row')[0]

    await userEvent.clear(within(row).getByRole('textbox', { name: 'code' }))
    expect(onChange).toHaveBeenLastCalledWith('/records/0/values/code', undefined)
    await userEvent.type(within(row).getByRole('textbox', { name: 'price' }), '1.5')
    expect(onChange).toHaveBeenLastCalledWith('/records/0/values/price', 1.5)
    await userEvent.type(within(row).getByRole('textbox', { name: 'due' }), 'x')
    expect(onChange).toHaveBeenLastCalledWith('/records/0/values/due', 'x')
  })

  it('adds a record with a new id and removes a record by its index', async () => {
    const onChange = renderGrid()

    await userEvent.click(screen.getByRole('button', { name: 'Add record' }))
    const [pointer, next] = onChange.mock.calls[0] as [string, { id: string; values: object }[]]
    expect(pointer).toBe('/records')
    expect(next).toHaveLength(3)
    expect(next.slice(0, 2)).toEqual(records)
    expect(next[2].id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(next[2].values).toEqual({})

    await userEvent.click(within(screen.getAllByTestId('studio-seed-row')[1]).getByRole('button', { name: 'Remove' }))
    expect(onChange).toHaveBeenLastCalledWith('/records/1', undefined)
  })
})
