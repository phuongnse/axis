import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import dayjs from 'dayjs'
import { afterEach, describe, expect, it, vi } from 'vitest'
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

  it('fills the width of its column with a date picker', () => {
    expect(show(field('date', {}), null).closest('.ant-picker')).toHaveStyle({ width: '100%' })
  })

  it('fills the width of its column with a date-time picker', () => {
    expect(show(field('date-time', {}), null).closest('.ant-picker')).toHaveStyle({ width: '100%' })
  })

  it('has no seconds column in the time panel', async () => {
    const input = show(field('date-time', {}), instant)

    await userEvent.click(input)

    expect(document.querySelectorAll('.ant-picker-time-panel-column')).toHaveLength(3)
  })
})

describe('FieldInput required', () => {
  const inputs: [string, Partial<FieldMetadata>, string][] = [
    ['text', {}, 'textbox'],
    ['enum', { values: ['open', 'closed'] }, 'combobox'],
    ['date', {}, 'textbox'],
  ]

  function renderInput(type: string, limits: Partial<FieldMetadata>, ariaRequired?: boolean) {
    render(
      <TextProvider catalogs={catalogs} development={false}>
        <FieldInput
          field={field(type as FieldType, limits)}
          value={null}
          onChange={() => undefined}
          ariaLabel="limited"
          locale="en"
          ariaRequired={ariaRequired}
        />
      </TextProvider>,
    )
  }

  it.each(inputs)('marks a required %s input aria-required', (type, limits, role) => {
    renderInput(type, limits, true)

    expect(screen.getByRole(role, { name: 'limited' })).toHaveAttribute('aria-required', 'true')
  })

  it.each(inputs)('leaves aria-required off a %s input that is not required', (type, limits, role) => {
    renderInput(type, limits)

    expect(screen.getByRole(role, { name: 'limited' })).not.toHaveAttribute('aria-required')
  })
})

describe('FieldInput description', () => {
  const inputs: [string, Partial<FieldMetadata>, string][] = [
    ['text', {}, 'textbox'],
    ['enum', { values: ['open', 'closed'] }, 'combobox'],
    ['date', {}, 'textbox'],
  ]

  it.each(inputs)('names the describing element on a %s input', (type, limits, role) => {
    render(
      <TextProvider catalogs={catalogs} development={false}>
        <FieldInput
          field={field(type as FieldType, limits)}
          value={null}
          onChange={() => undefined}
          ariaLabel="limited"
          locale="en"
          ariaDescribedBy="limited-help"
        />
      </TextProvider>,
    )

    expect(screen.getByRole(role, { name: 'limited' })).toHaveAttribute('aria-describedby', 'limited-help')
  })
})

describe('FieldInput user picker', () => {
  const people = [
    { id: 'u1', displayName: 'Ann Lee' },
    { id: 'u2', displayName: 'Bob Ray' },
  ]

  // The route answers the `id` lookup with that person, and a search with the people whose name contains the text.
  const fetchMock = vi.fn((input: string) => {
    const query = new URL(input, 'http://localhost').searchParams
    const id = query.get('id')
    const search = (query.get('search') ?? '').toLowerCase()
    const items =
      id !== null
        ? people.filter((person) => person.id === id)
        : people.filter((person) => person.displayName.toLowerCase().includes(search))
    return Promise.resolve(new Response(JSON.stringify({ items }), { status: 200 }))
  })

  afterEach(() => {
    fetchMock.mockClear()
    vi.unstubAllGlobals()
  })

  function renderPicker(value: string | null, onChange: (value: unknown) => void = () => undefined, required = false) {
    vi.stubGlobal('fetch', fetchMock)
    render(
      <TextProvider catalogs={catalogs} development={false}>
        <FieldInput
          field={field('text', { usersPath: '/api/u', required })}
          value={value}
          onChange={onChange}
          ariaLabel="limited"
          locale="en"
        />
      </TextProvider>,
    )
    return screen.getByRole('combobox', { name: 'limited' })
  }

  const requested = () => fetchMock.mock.calls.map(([input]) => input)

  it('shows the name of the stored id', async () => {
    const picker = renderPicker('u1')

    await waitFor(() => expect(picker.closest('.ant-select')).toHaveTextContent('Ann Lee'))
    expect(requested()).toContain('/api/u?id=u1')
  })

  it('shows the stored id itself when no profile has it', async () => {
    const picker = renderPicker('gone')

    await waitFor(() => expect(requested()).toContain('/api/u?id=gone'))
    expect(picker.closest('.ant-select')).toHaveTextContent('gone')
  })

  it('searches with the typed text and sends the picked id', async () => {
    const onChange = vi.fn()
    const picker = renderPicker('u1', onChange)

    await userEvent.type(picker, 'bob')
    await waitFor(() => expect(requested()).toContain('/api/u?search=bob'))
    await userEvent.click(await screen.findByRole('option', { name: 'Bob Ray' }))

    expect(onChange).toHaveBeenCalledWith('u2')
  })

  it('clears a picker that is not required to null', async () => {
    const onChange = vi.fn()
    const picker = renderPicker('u1', onChange)
    await waitFor(() => expect(picker.closest('.ant-select')).toHaveTextContent('Ann Lee'))

    await userEvent.hover(picker.closest('.ant-select')!)
    await userEvent.click(screen.getByRole('button', { name: 'Clear' }))

    expect(onChange).toHaveBeenCalledWith(null)
  })

  it('cannot clear a required picker', async () => {
    const picker = renderPicker('u1', () => undefined, true)
    await waitFor(() => expect(picker.closest('.ant-select')).toHaveTextContent('Ann Lee'))

    expect(picker.closest('.ant-select')!.querySelector('.ant-select-clear')).toBeNull()
    expect(picker).toHaveAttribute('aria-required', 'true')
  })
})
