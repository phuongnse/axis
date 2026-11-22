import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import { useState } from 'react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import { ChildCollectionTable } from './ChildCollectionTable'
import { designTokensFor } from './designTokens'
import { RecordPagesContext } from './recordPages'
import type { RecordRow, RowLabels } from './records'
import type { FieldMetadata, FieldType } from './site'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'
import { withAntdStyles } from '../test/antdStyles'

const texts = {
  'test.lines': 'Lines',
  'test.description': 'Description',
  'test.quantity': 'Quantity',
  'shell.form.addRow': 'Add row',
  'shell.form.choose': 'Choose…',
  'shell.form.clear': 'Clear',
  'shell.form.openRecord': 'Open',
  'test.category': 'Category',
  'shell.form.removeRow': 'Delete row',
  'shell.table.empty': 'No records yet.',
  'shell.table.rowActions': 'Actions',
}
const catalogs = [{ texts, fallbackTexts: texts }]

function field(
  name: string,
  type: FieldType,
  labelKey: string | null,
  fields: FieldMetadata[] | null = null,
): FieldMetadata {
  return {
    name,
    type,
    labelKey,
    required: false,
    unique: false,
    computed: false,
    sequence: false,
    creator: false,
    maxLength: null,
    precision: null,
    scale: null,
    min: null,
    max: null,
    minLength: null,
    values: null,
    target: null,
    fields,
  }
}

const lines = field('lines', 'child-collection', 'test.lines', [
  field('description', 'text', 'test.description'),
  field('quantity', 'integer', 'test.quantity'),
])

// Holds the rows and their labels, as the form does, so an added row is rendered.
function Harness({
  initial = [],
  initialLabels = [],
  errors = {},
  readOnly = false,
  collection = lines,
}: {
  initial?: RecordRow[]
  initialLabels?: RowLabels[]
  errors?: Record<string, string[]>
  readOnly?: boolean
  collection?: FieldMetadata
}) {
  const [rows, setRows] = useState<RecordRow[]>(initial)
  const [labels, setLabels] = useState<RowLabels[]>(initialLabels)
  return (
    <ChildCollectionTable
      field={collection}
      rows={rows}
      labels={labels}
      errors={errors}
      locale="en"
      readOnly={readOnly}
      onChange={(nextRows, nextLabels) => {
        setRows(nextRows)
        setLabels(nextLabels)
      }}
    />
  )
}

function renderIn(mode: ThemeMode, ui: React.ReactNode) {
  return render(
    <ThemeModeProvider initialMode={mode}>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <TextProvider catalogs={catalogs} development={false}>
          {ui}
        </TextProvider>
      </ConfigProvider>
    </ThemeModeProvider>,
  )
}

const oneRow = [{ description: 'Paper', quantity: '1' }]

describe('the add and remove buttons of a child collection', () => {
  it('puts Add row before the table in document order', () => {
    renderIn('light', <Harness />)

    const addRow = screen.getByRole('button', { name: 'Add row' })
    const table = screen.getByRole('table')
    expect(addRow.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('moves focus to the first input of each added row', async () => {
    renderIn('light', <Harness />)

    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))
    await userEvent.click(screen.getByRole('button', { name: 'Add row' }))

    expect(screen.getByRole('textbox', { name: 'Description 2' })).toHaveFocus()
  })

  it('draws Delete row in dangerText in dark mode and shows its name as a tooltip on focus', async () => {
    renderIn('dark', <Harness initial={oneRow} />)

    const remove = screen.getByRole('button', { name: 'Delete row' })
    expect(remove).toHaveStyle({ color: designTokensFor('dark').colour.dangerText })
    await userEvent.click(screen.getByRole('textbox', { name: 'Quantity 1' }))
    await userEvent.tab()

    expect(remove).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Delete row')
  })
})

describe('the cells of a child collection', () => {
  describe('with antd styles', () => {
    withAntdStyles()

    it('shows a cell message as a left-aligned block 4px under its input, with no gap below', () => {
      renderIn('light', <Harness initial={oneRow} errors={{ '0/quantity': ['Bad'] }} />)

      const message = screen.getByTestId('field-error-lines-0-quantity')
      expect(message).toHaveStyle({ display: 'block', textAlign: 'left' })
      expect(message.closest('.ant-form-item-explain')).toHaveStyle({ marginTop: '4px' })
      const item = message.closest('.ant-form-item')
      expect(item && getComputedStyle(item).getPropertyValue('--ant-form-item-margin-bottom')).toBe('0px')
    })
  })

  it('marks the input of a cell in error and no other cell', () => {
    const rows = [
      { description: 'Paper', quantity: '1' },
      { description: 'Pen', quantity: '2' },
    ]
    renderIn('light', <Harness initial={rows} errors={{ '1/quantity': ['Bad'] }} />)

    expect(screen.getByRole('textbox', { name: 'Quantity 2' })).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByRole('textbox', { name: 'Quantity 1' })).not.toHaveAttribute('aria-invalid')
    expect(screen.getByRole('textbox', { name: 'Description 2' })).not.toHaveAttribute('aria-invalid')
  })

  it('lines up the cells of a row at the top', () => {
    renderIn('light', <Harness initial={oneRow} />)

    expect(screen.getByRole('textbox', { name: 'Description 1' }).closest('td')).toHaveStyle({
      verticalAlign: 'top',
    })
  })

  it('shows the em dash in an empty cell of a read-only collection', () => {
    renderIn('light', <Harness readOnly initial={[{ description: null, quantity: '1' }]} />)

    expect(screen.getByTestId('cell-lines-0-description')).toHaveTextContent('—')
    expect(screen.queryByRole('button', { name: 'Delete row' })).not.toBeInTheDocument()
  })
})

describe('the reference cells of a child collection', () => {
  const categoryId = '0b9e8d7c-6a5f-4e3d-9c2b-1a0f9e8d7c6b'
  const category = {
    ...field('category', 'reference', 'test.category'),
    target: { entity: 'Category', displayField: 'name', recordsPath: '/api/categories' },
  }
  const withCategory = field('lines', 'child-collection', 'test.lines', [
    field('description', 'text', 'test.description'),
    category,
  ])
  const rows = [{ description: 'Paper', category: categoryId }]
  const labels = [{ category: 'Office' }]

  function renderWithPages(ui: React.ReactNode, recordPages: Record<string, string> | null) {
    return renderIn(
      'light',
      <MemoryRouter initialEntries={['/sample/request/1']}>
        <RecordPagesContext value={recordPages ? { sitePath: 'sample', recordPages } : null}>{ui}</RecordPagesContext>
      </MemoryRouter>,
    )
  }

  it('shows a read-only reference cell as a link to its record page', () => {
    renderWithPages(<Harness readOnly collection={withCategory} initial={rows} initialLabels={labels} />, {
      Category: '/sample/records/category',
    })

    expect(screen.getByRole('link', { name: 'Office' })).toHaveAttribute(
      'href',
      `/sample/records/category/${categoryId}`,
    )
  })

  it('shows a read-only reference cell as text when its entity has no record page or its label is missing', () => {
    renderWithPages(<Harness readOnly collection={withCategory} initial={rows} initialLabels={labels} />, {})
    expect(screen.getByTestId('cell-lines-0-category')).toHaveTextContent('Office')
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    cleanup()

    renderWithPages(<Harness readOnly collection={withCategory} initial={rows} />, {
      Category: '/sample/records/category',
    })
    expect(screen.getByTestId('cell-lines-0-category')).toHaveTextContent(categoryId)
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('joins an Open link after Choose and before Clear in an editable reference cell', () => {
    renderWithPages(<Harness collection={withCategory} initial={rows} initialLabels={labels} />, {
      Category: '/sample/records/category',
    })

    const cell = screen.getByTestId('reference-lines-0-category')
    expect(Array.from(cell.querySelectorAll('button, a')).map((control) => control.getAttribute('aria-label'))).toEqual(
      ['Choose… Category 1', 'Open Office', 'Clear Category 1'],
    )
    expect(screen.getByRole('link', { name: 'Open Office' })).toHaveAttribute(
      'href',
      `/sample/records/category/${categoryId}`,
    )
  })

  it('draws no Open link in an editable cell outside a site or with no record chosen', () => {
    renderWithPages(<Harness collection={withCategory} initial={rows} initialLabels={labels} />, null)
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    cleanup()

    renderWithPages(<Harness collection={withCategory} initial={[{ description: 'Paper', category: null }]} />, {
      Category: '/sample/records/category',
    })
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })
})
