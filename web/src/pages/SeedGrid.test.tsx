import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState, type ReactNode } from 'react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import platformEnglish from '../../../src/Axis.Presentation/Texts/en.json'
import { setAt } from '../platform/jsonPatch'
import { TextProvider } from '../platform/TextProvider'
import { seedProblems, type SeedField } from '../platform/seedValues'
import { ThemeModeProvider } from '../platform/theme'
import { useThemeMode } from '../platform/themeMode'
import { chooseOption, optionTexts } from '../test/antd'
import { clickInDialog, dialog, pick } from '../test/tableFilter'
import { SeedGrid } from './SeedGrid'

const fields: SeedField[] = [
  { name: 'code', type: 'text', required: true, maxLength: 20 },
  { name: 'size', type: 'integer' },
  { name: 'price', type: 'decimal', precision: 8, scale: 2 },
  { name: 'cost', type: 'money' },
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

const columns = fields.filter(
  (field) => !field.expression && !field.sequence && !field.creator && field.type !== 'child-collection',
)

/** The ids of the records as last saved. */
const savedIds = records.map((record) => record.id)

function renderGrid(onChange = vi.fn(), data: unknown = records) {
  render(
    <MemoryRouter>
      <ThemeModeProvider>
        <ThemeRoot>
          <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
            <SeedGrid
              fields={fields}
              records={data}
              savedIds={savedIds}
              problems={seedProblems(data, columns)}
              onChange={onChange}
            />
          </TextProvider>
        </ThemeRoot>
      </ThemeModeProvider>
    </MemoryRouter>,
  )
  return onChange
}

/** The grid over a draft that it edits, as the editor does: each change is applied with `setAt`. */
function renderEditing(address = '/') {
  function Harness() {
    const [draft, setDraft] = useState<unknown>({ records })
    return (
      <>
        <SeedGrid
          fields={fields}
          records={(draft as { records: unknown }).records}
          savedIds={savedIds}
          problems={seedProblems((draft as { records: unknown }).records, columns)}
          onChange={(pointer, next) => setDraft((current: unknown) => setAt(current, pointer, next))}
        />
        <output data-testid="seed-records">{JSON.stringify(draft)}</output>
      </>
    )
  }
  render(
    <MemoryRouter initialEntries={[address]}>
      <TextProvider catalogs={[{ texts: platformEnglish, fallbackTexts: platformEnglish }]} development={false}>
        <Harness />
      </TextProvider>
    </MemoryRouter>,
  )
  return () => (JSON.parse(screen.getByTestId('seed-records').textContent ?? '') as { records: typeof records }).records
}

/** Filters the grid on a text column that is `value`. */
async function filterOn(column: string, operator: string, value: string) {
  await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
  await pick('Column', 0, column)
  await pick('Operator', 0, operator)
  await userEvent.type(within(dialog()).getByRole('textbox', { name: 'Value' }), value)
  await clickInDialog('Apply')
}

afterEach(() => localStorage.clear())

describe.each(['light', 'dark'])('SeedGrid in %s mode', (mode) => {
  beforeEach(() => localStorage.setItem('axis.themeMode', mode))

  it('renders inside the theme root with its headers, values and error state', () => {
    renderGrid()

    const grid = screen.getByTestId('studio-seed-grid')
    expect(grid.closest(`[data-theme-mode=${mode}]`)).not.toBeNull()
    expect(grid).toBeVisible()
    for (const name of ['ID', 'code', 'size', 'price', 'active', 'status', 'due', 'closedAt', 'manager']) {
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
    expect(within(cell).getByTestId('studio-seed-problem')).toHaveTextContent('Enter a value.')
  })

  it('removes the key when a cell is cleared and sets a typed number', async () => {
    const onChange = renderGrid()
    const row = screen.getAllByTestId('studio-seed-row')[0]

    await userEvent.clear(within(row).getByRole('textbox', { name: 'code' }))
    expect(onChange).toHaveBeenLastCalledWith('/records/0/values/code', undefined)
    await userEvent.type(within(row).getByRole('textbox', { name: 'price' }), '1.5')
    expect(onChange).toHaveBeenLastCalledWith('/records/0/values/price', 1.5)
    await userEvent.type(within(row).getByRole('textbox', { name: 'cost' }), '1500')
    expect(onChange).toHaveBeenLastCalledWith('/records/0/values/cost', 1500)
    await userEvent.type(within(row).getByRole('textbox', { name: 'due' }), 'x')
    expect(onChange).toHaveBeenLastCalledWith('/records/0/values/due', 'x')
  })

  it('adds a record with a new id and deletes a record by its index', async () => {
    const onChange = renderGrid()

    await userEvent.click(screen.getByRole('button', { name: 'Add record' }))
    const [pointer, next] = onChange.mock.calls[0] as [string, { id: string; values: object }[]]
    expect(pointer).toBe('/records')
    expect(next).toHaveLength(3)
    expect(next.slice(0, 2)).toEqual(records)
    expect(next[2].id).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/)
    expect(next[2].values).toEqual({})

    await userEvent.click(within(screen.getAllByTestId('studio-seed-row')[1]).getByRole('button', { name: 'Delete' }))
    expect(onChange).toHaveBeenLastCalledWith('/records/1', undefined)
  })

  describe('filter', () => {
    const shownCodes = () =>
      screen.getAllByTestId('studio-seed-row').map((row) => within(row).getByRole('textbox', { name: 'code' }))

    it('offers the ID and every column that a seed may set, each by its kind of value', async () => {
      renderEditing()

      await userEvent.click(screen.getByRole('button', { name: 'Filter' }))
      await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Column' }))
      const offered = optionTexts()
      expect(offered).toEqual(['ID', 'code', 'size', 'price', 'cost', 'active', 'status', 'due', 'closedAt', 'manager'])
      await chooseOption('status')
      await userEvent.click(within(dialog()).getByRole('combobox', { name: 'Value' }))
      expect(optionTexts()).toContain('Closed')
    })

    it('hides a record that does not match and shows every row on one page with no pager', async () => {
      renderEditing()

      await filterOn('code', 'is', 'ENG')

      expect(shownCodes()).toHaveLength(1)
      expect(shownCodes()[0]).toHaveValue('ENG')
      expect(screen.queryByRole('list')).not.toBeInTheDocument()
    })

    it('keeps an edit to a record that the filter then hides, and shows the record again after Clear all', async () => {
      const draft = renderEditing()
      await userEvent.type(
        within(screen.getAllByTestId('studio-seed-row')[0]).getByRole('textbox', { name: 'code' }),
        'X',
      )

      await filterOn('code', 'is', 'ENG')
      expect(shownCodes().map((input) => (input as HTMLInputElement).value)).toEqual(['ENG'])
      expect(draft()[0].values.code).toBe('FINX')

      await userEvent.click(screen.getByRole('button', { name: 'Clear all' }))
      expect(shownCodes().map((input) => (input as HTMLInputElement).value)).toEqual(['FINX', 'ENG'])
    })

    it('shows a record added while a filter is in force', async () => {
      const draft = renderEditing()
      await filterOn('code', 'is', 'ENG')
      expect(shownCodes()).toHaveLength(1)

      await userEvent.click(screen.getByRole('button', { name: 'Add record' }))

      expect(draft()).toHaveLength(3)
      expect(screen.getAllByTestId('studio-seed-row')).toHaveLength(2)
    })

    it('reads the filter from the address, so a shared link shows the same rows', () => {
      const filter = encodeURIComponent(JSON.stringify({ all: [{ field: 'code', op: 'eq', value: 'FIN' }] }))
      renderEditing(`/?records.filter=${filter}`)

      expect(shownCodes().map((input) => (input as HTMLInputElement).value)).toEqual(['FIN'])
    })
  })
})
