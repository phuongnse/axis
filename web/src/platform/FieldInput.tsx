import { Checkbox, DatePicker, Input, Select, Space } from 'antd'
import dayjs from 'dayjs'
import type { CSSProperties, KeyboardEvent } from 'react'
import { FileInput } from './FileInput'
import { enumLabel, dateParts, dateTimePickerFormat, pickerFormat, timeParts } from './formatValue'
import { isFileValue, type RecordValue } from './records'
import type { FieldMetadata } from './site'
import { useText } from './texts'

interface FieldInputProps {
  /** A field of a type with a plain value: not a reference and not a child collection. */
  field: FieldMetadata
  value: RecordValue
  onChange: (value: RecordValue) => void
  id?: string
  /** The accessible name, for an input that has no label element of its own. */
  ariaLabel?: string
  /** The input's style, such as a fixed width where the layout gives it none. */
  style?: CSSProperties
  /** Proposed: the locale whose short numeric form a date or date-time picker shows. Left out, the browser's. */
  locale?: string
  /** The currency code of a money field, shown beside the box. Left out, nothing is shown. */
  currency?: string | null
}

// Enter in a picker only confirms the picked value. The picker handles it first, so stopping the
// default here only stops the browser's implicit form submission.
function confirmOnly(event: KeyboardEvent) {
  if (event.key === 'Enter') {
    event.preventDefault()
  }
}

// The record API writes six fraction digits, and the picker works to the second.
function dateTimeValue(value: RecordValue) {
  return value === null ? null : dayjs(String(value).replace(/\.\d+(?=Z$)/, ''))
}

// The limits as symbols and numbers, so they need no translated text: `1 – 10`, `≥ 1` or `≤ 10`.
function rangeText(min: string | null, max: string | null) {
  if (min !== null && max !== null) {
    return `${min} – ${max}`
  }
  return min !== null ? `≥ ${min}` : max !== null ? `≤ ${max}` : undefined
}

// Whether the typed text is a number outside the limits. Other text is left to the server to refuse.
function outsideRange(value: RecordValue, field: FieldMetadata) {
  const number = value === null || String(value).trim() === '' ? NaN : Number(value)
  if (!Number.isFinite(number)) {
    return false
  }
  return (field.min !== null && number < Number(field.min)) || (field.max !== null && number > Number(field.max))
}

/** The input for one value of a field, the same in the form and in a child collection's cells. */
export function FieldInput({
  field,
  value,
  onChange,
  id,
  ariaLabel,
  style,
  locale = navigator.language,
  currency,
}: FieldInputProps) {
  const t = useText()
  const text = (event: { target: { value: string } }) => onChange(event.target.value || null)
  switch (field.type) {
    case 'text':
      return (
        <Input
          id={id}
          aria-label={ariaLabel}
          style={style}
          value={value === null ? '' : String(value)}
          maxLength={field.maxLength ?? undefined}
          minLength={field.minLength ?? undefined}
          showCount={
            field.minLength === null
              ? undefined
              : {
                  formatter: ({ count }) =>
                    `${count} / ${field.maxLength === null ? `≥${field.minLength}` : `${field.minLength}–${field.maxLength}`}`,
                }
          }
          onChange={text}
        />
      )
    case 'integer':
    case 'decimal':
    case 'money': {
      // Plain text inputs keep the typed text unchanged, so no digit is lost. A value outside the
      // limits is marked but never blocked, so 150 can be typed when the minimum is 100.
      const invalid = outsideRange(value, field)
      const input = (
        <Input
          id={id}
          aria-label={ariaLabel}
          aria-invalid={invalid || undefined}
          status={invalid ? 'error' : undefined}
          placeholder={rangeText(field.min, field.max)}
          style={currency ? undefined : style}
          inputMode={field.type === 'integer' ? 'numeric' : 'decimal'}
          value={value === null ? '' : String(value)}
          onChange={text}
        />
      )
      return currency ? (
        <Space.Compact block style={style}>
          {input}
          <Space.Addon>{currency}</Space.Addon>
        </Space.Compact>
      ) : (
        input
      )
    }
    case 'boolean':
      return (
        <Checkbox
          id={id}
          aria-label={ariaLabel}
          style={style}
          checked={value === true}
          onChange={(event) => onChange(event.target.checked)}
        />
      )
    case 'date':
      return (
        <DatePicker
          id={id}
          aria-label={ariaLabel}
          // Proposed: a picker fills its column, as every other input does.
          style={{ width: '100%', ...style }}
          format={pickerFormat(locale, dateParts)}
          minDate={field.min === null ? undefined : dayjs(field.min)}
          maxDate={field.max === null ? undefined : dayjs(field.max)}
          placeholder={rangeText(field.min, field.max)}
          value={value === null ? null : dayjs(String(value))}
          onChange={(date) => onChange(date ? date.format('YYYY-MM-DD') : null)}
          onKeyDown={confirmOnly}
        />
      )
    case 'date-time':
      return (
        <DatePicker
          id={id}
          aria-label={ariaLabel}
          // Proposed: a picker fills its column, as every other input does.
          style={{ width: '100%', ...style }}
          showTime={{ format: pickerFormat(locale, timeParts(locale)) }}
          format={dateTimePickerFormat(locale)}
          minDate={field.min === null ? undefined : dateTimeValue(field.min)!}
          maxDate={field.max === null ? undefined : dateTimeValue(field.max)!}
          placeholder={rangeText(field.min, field.max)}
          value={dateTimeValue(value)}
          // UTC to the second, as an RFC 3339 string the record API accepts.
          onChange={(date) => onChange(date ? new Date(date.valueOf()).toISOString().replace(/\.\d{3}Z$/, 'Z') : null)}
          onKeyDown={confirmOnly}
        />
      )
    case 'enum':
      return (
        <Select
          id={id}
          aria-label={ariaLabel}
          style={style}
          allowClear
          // Without a virtual list, each shown option is an `option` with `aria-selected`.
          virtual={false}
          value={value === null ? null : String(value)}
          options={(field.values ?? []).map((option) => ({ value: option, label: enumLabel(field, option, t) }))}
          onChange={(option: string | undefined) => onChange(option ?? null)}
        />
      )
    case 'file':
      return (
        <FileInput
          field={field}
          id={id}
          value={isFileValue(value) ? value : null}
          label={ariaLabel ?? field.name}
          onChange={onChange}
        />
      )
    case 'reference':
    case 'references':
    case 'child-collection':
      // The form renders these itself, and a child entity has neither.
      return null
  }
}
