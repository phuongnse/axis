import { Checkbox, DatePicker, Input, Select } from 'antd'
import dayjs from 'dayjs'
import type { CSSProperties, KeyboardEvent } from 'react'
import type { RecordValue } from './records'
import type { FieldMetadata } from './site'

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

/** The input for one value of a field, the same in the form and in a child collection's cells. */
export function FieldInput({ field, value, onChange, id, ariaLabel, style }: FieldInputProps) {
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
          onChange={text}
        />
      )
    case 'integer':
    case 'decimal':
      // Plain text inputs keep the typed text unchanged, so no digit is lost.
      return (
        <Input
          id={id}
          aria-label={ariaLabel}
          style={style}
          inputMode={field.type === 'integer' ? 'numeric' : 'decimal'}
          value={value === null ? '' : String(value)}
          onChange={text}
        />
      )
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
          style={style}
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
          style={style}
          showTime
          format="YYYY-MM-DD HH:mm:ss"
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
          value={value === null ? null : String(value)}
          options={(field.values ?? []).map((option) => ({ value: option, label: option }))}
          onChange={(option: string | undefined) => onChange(option ?? null)}
        />
      )
    case 'reference':
    case 'child-collection':
      // The form renders these itself, and a child entity has neither.
      return null
  }
}
