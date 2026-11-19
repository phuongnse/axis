import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { designTokensFor } from './designTokens'
import { TextProvider } from './TextProvider'
import { ValueTable } from './ValueTable'
import { JsonNumber, valueRows } from './valueRows'

const texts = {
  'shell.value.empty': 'Empty',
  'shell.value.hidden': 'Hidden',
  'shell.value.field': '1 field',
  'shell.value.fields': '{count} fields',
  'shell.value.item': '1 item',
  'shell.value.items': '{count} items',
  'shell.empty.noData': 'No data yet.',
}

function renderTable(value: unknown) {
  return render(
    <TextProvider catalogs={[{ texts, fallbackTexts: texts }]} development={false}>
      <ValueTable value={value} testId="values" />
    </TextProvider>,
  )
}

const { colour } = designTokensFor('light')
const emptyStyle = { fontStyle: 'italic', color: colour.textMuted }

describe('valueRows', () => {
  it('names a member of an object by its key and a member of a list by its index, and gives a nested value a row', () => {
    const names = valueRows({ number: 7, lines: [{ description: 'Paper' }] }).map((row) => row.name)

    expect(names).toEqual(['number', 'lines', 'lines[0]', 'lines[0].description'])
  })

  it('names the members of a list at the root by their index', () => {
    expect(valueRows(['a', 'b']).map((row) => row.name)).toEqual(['[0]', '[1]'])
  })

  it('shows a number by the digits it was written with', () => {
    expect(valueRows({ big: new JsonNumber('12345678901234567890') })[0].text).toBe('12345678901234567890')
  })
})

describe('ValueTable', () => {
  it('draws no header row', () => {
    const { container } = renderTable({ name: 'Paper' })

    expect(container.querySelector('thead')).toBeNull()
    expect(screen.queryByRole('columnheader')).not.toBeInTheDocument()
  })

  it('gives each name 5 of 12 columns in the muted text colour and each value 7', () => {
    renderTable({ name: 'Paper' })

    expect(screen.getByRole('cell', { name: 'name' })).toHaveStyle({
      width: `${(5 / 12) * 100}%`,
      color: colour.textMuted,
    })
    expect(screen.getByRole('cell', { name: 'Paper' })).toHaveStyle({ width: `${(7 / 12) * 100}%` })
  })

  it('draws a nested value as a row named by its path', () => {
    renderTable({ lines: [{ description: 'Paper' }] })

    const row = screen.getByRole('cell', { name: 'lines[0].description' }).closest('tr') as HTMLElement
    expect(within(row).getByRole('cell', { name: 'Paper' })).toBeInTheDocument()
  })

  it('draws null, Empty, a count of fields or items and Hidden for a secret in the empty value style', () => {
    renderTable({ none: null, object: {}, list: [], secret: '[secret]', pair: { a: 1, b: 2 }, three: [1, 2, 3] })

    for (const word of ['null', 'Hidden', '2 fields', '3 items']) {
      expect(screen.getByText(word)).toHaveStyle(emptyStyle)
    }
    expect(screen.getAllByText('Empty')).toHaveLength(2)
    for (const word of screen.getAllByText('Empty')) {
      expect(word).toHaveStyle(emptyStyle)
    }
  })

  it('reads one field or one item in the singular', () => {
    renderTable({ one: { a: 1 }, list: [1] })

    expect(screen.getByText('1 field')).toBeInTheDocument()
    expect(screen.getByText('1 item')).toBeInTheDocument()
  })

  it('draws a typed value as written, not in the empty value style', () => {
    renderTable({ done: false, count: 3, note: 'Paper' })

    for (const text of ['false', '3', 'Paper']) {
      expect(screen.getByText(text)).not.toHaveStyle({ fontStyle: 'italic' })
    }
  })
})
