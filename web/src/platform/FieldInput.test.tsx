import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { describe, expect, it, vi } from 'vitest'
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

function show(
  metadata: FieldMetadata,
  value: string | null,
  locale = 'en',
  onChange: (value: unknown) => void = () => undefined,
) {
  render(
    <TextProvider catalogs={catalogs} development={false}>
      <FieldInput field={metadata} value={value} onChange={onChange} ariaLabel="limited" locale={locale} />
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

describe('FieldInput dates', () => {
  // The picker shows the instant in the browser's time zone.
  const instant = '2026-03-04T16:30:45.123456Z'

  it('shows a date in the pattern of the locale', () => {
    expect(show(field('date', {}), '2026-03-18')).toHaveValue('03/18/2026')
  })

  it('shows a date in the day-first pattern of vi', () => {
    expect(show(field('date', {}), '2026-03-18', 'vi')).toHaveValue('18/03/2026')
  })

  it('shows a date-time to the minute, with no seconds, in the pattern of the locale', () => {
    expect(show(field('date-time', {}), instant)).toHaveValue(dayjs(instant).format('MM/DD/YYYY h:mm A'))
  })

  it('shows a date-time on the 24-hour clock of vi', () => {
    expect(show(field('date-time', {}), instant, 'vi')).toHaveValue(dayjs(instant).format('DD/MM/YYYY HH:mm'))
  })

  it('sends a picked date-time in UTC with zero seconds', async () => {
    const onChange = vi.fn()
    const input = show(field('date-time', {}), null, 'en', onChange)

    await userEvent.type(input, '03/04/2026 4:30 PM{Enter}')

    expect(onChange).toHaveBeenLastCalledWith(dayjs('2026-03-04T16:30:00').toDate().toISOString().replace('.000Z', 'Z'))
  })

  it('has no seconds column in the time panel', async () => {
    const input = show(field('date-time', {}), instant)

    await userEvent.click(input)

    expect(document.querySelectorAll('.ant-picker-time-panel-column')).toHaveLength(3)
  })
})
