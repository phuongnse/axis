import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { FieldInput } from './FieldInput'
import type { FieldMetadata, FieldType } from './site'
import { TextProvider } from './TextProvider'

const catalogs = [{ texts: {}, fallbackTexts: {} }]

function field(type: FieldType, limits: Partial<FieldMetadata>): FieldMetadata {
  return {
    name: 'limited',
    type,
    labelKey: null,
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
    fields: null,
    ...limits,
  }
}

function show(metadata: FieldMetadata, value: string | null) {
  render(
    <TextProvider catalogs={catalogs} development={false}>
      <FieldInput field={metadata} value={value} onChange={() => undefined} ariaLabel="limited" />
    </TextProvider>,
  )
  return screen.getByRole('textbox', { name: 'limited' })
}

describe('FieldInput limits', () => {
  const count = field('integer', { min: '1', max: '10' })

  it('shows the range of an integer and marks a value outside it', () => {
    expect(show(count, null)).toHaveAttribute('placeholder', '1 – 10')
  })

  it('marks an integer above the maximum as invalid', () => {
    expect(show(count, '11')).toHaveAttribute('aria-invalid', 'true')
  })

  it('marks an integer below the minimum as invalid', () => {
    expect(show(count, '0')).toHaveAttribute('aria-invalid', 'true')
  })

  it('accepts an integer on the maximum', () => {
    expect(show(count, '10')).not.toHaveAttribute('aria-invalid')
  })

  it('leaves text that is not a number to the server', () => {
    expect(show(count, '-')).not.toHaveAttribute('aria-invalid')
  })

  it('shows a single limit of a decimal', () => {
    expect(show(field('decimal', { min: '0.5' }), null)).toHaveAttribute('placeholder', '≥ 0.5')
  })

  it('shows the range of a date', () => {
    const input = show(field('date', { min: '2026-01-01', max: '2026-12-31' }), null)

    expect(input).toHaveAttribute('placeholder', '2026-01-01 – 2026-12-31')
  })

  it('shows the minimum length of a text field and its count', () => {
    const input = show(field('text', { minLength: 3, maxLength: 10 }), null)

    expect(input).toHaveAttribute('minlength', '3')
    expect(screen.getByText('0 / 3–10')).toBeInTheDocument()
  })

  it('shows the minimum length of a text field with no maximum', () => {
    show(field('text', { minLength: 3 }), 'ab')

    expect(screen.getByText('2 / ≥3')).toBeInTheDocument()
  })

  it('shows no limits for a field without any', () => {
    const input = show(field('text', {}), null)

    expect(input).not.toHaveAttribute('minlength')
    expect(screen.queryByText(/ \/ /)).not.toBeInTheDocument()
  })
})
