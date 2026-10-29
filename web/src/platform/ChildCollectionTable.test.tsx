import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConfigProvider } from 'antd'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { ChildCollectionTable } from './ChildCollectionTable'
import { designTokensFor } from './designTokens'
import type { RecordRow, RowLabels } from './records'
import type { FieldMetadata, FieldType } from './site'
import { TextProvider } from './TextProvider'
import { ThemeModeProvider } from './theme'
import type { ThemeMode } from './themeMode'

const texts = {
  'test.lines': 'Lines',
  'test.description': 'Description',
  'test.quantity': 'Quantity',
  'shell.form.addRow': 'Add row',
  'shell.form.removeRow': 'Remove row',
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
function Harness({ initial = [], errors = {} }: { initial?: RecordRow[]; errors?: Record<string, string[]> }) {
  const [rows, setRows] = useState<RecordRow[]>(initial)
  const [labels, setLabels] = useState<RowLabels[]>([])
  return (
    <ChildCollectionTable
      field={lines}
      rows={rows}
      labels={labels}
      errors={errors}
      locale="en"
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

  it('draws Remove row in dangerText in dark mode and shows its name as a tooltip on focus', async () => {
    renderIn('dark', <Harness initial={oneRow} />)

    const remove = screen.getByRole('button', { name: 'Remove row' })
    expect(remove).toHaveStyle({ color: designTokensFor('dark').colour.dangerText })
    await userEvent.click(screen.getByRole('textbox', { name: 'Quantity 1' }))
    await userEvent.tab()

    expect(remove).toHaveFocus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Remove row')
  })
})

describe('the cells of a child collection', () => {
  it('shows a cell message as a left-aligned block 4px under its input', () => {
    renderIn('light', <Harness initial={oneRow} errors={{ '0/quantity': ['Bad'] }} />)

    expect(screen.getByTestId('field-error-lines-0-quantity')).toHaveStyle({
      display: 'block',
      marginTop: '4px',
      textAlign: 'left',
    })
  })

  it('lines up the cells of a row at the top', () => {
    renderIn('light', <Harness initial={oneRow} />)

    expect(screen.getByRole('textbox', { name: 'Description 1' }).closest('td')).toHaveStyle({
      verticalAlign: 'top',
    })
  })
})
