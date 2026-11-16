import { Input } from 'antd'
import { useContext } from 'react'
import { AxisField } from '../platform/AxisField'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { ExpressionCheckContext } from './expressionCheckContext'
import { ExpressionField } from './ExpressionField'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/**
 * The editor of an expression: the expression editor when the draft can check expressions, a plain box otherwise. The
 * `id`, `aria-describedby` and `aria-required` that `AxisField` gives its input reach the plain box only, as the
 * expression editor does not take them yet. It keeps its own name. A `readOnly` expression is always the plain box.
 */
export function Expression({
  pointer,
  name,
  id,
  'aria-describedby': describedBy,
  'aria-required': ariaRequired,
  value,
  required = true,
  readOnly = false,
  onChange,
}: {
  pointer: string
  name: string
  id?: string
  'aria-describedby'?: string
  'aria-required'?: boolean
  value: string
  required?: boolean
  readOnly?: boolean
  onChange: ChangeHandler
}) {
  const check = useContext(ExpressionCheckContext)

  return (
    <ExpressionField
      check={readOnly ? null : check}
      pointer={pointer}
      name={name}
      id={id}
      aria-describedby={describedBy}
      aria-required={ariaRequired}
      value={value}
      required={required}
      onChange={onChange}
    />
  )
}

/**
 * A field with a picker over the application's names of one kind, or a plain box while the names have not loaded. An
 * empty `help` draws no help line.
 */
export function NamePicker({
  pointer,
  label,
  help,
  kind,
  required = false,
  value,
  onPick,
}: {
  pointer: string
  label: string
  help: string
  kind: string
  /** Marks the field required, as for {@link AxisField}. Off unless the schema requires the key. */
  required?: boolean
  value: unknown
  onPick: (next: string) => void
}) {
  const names = useContext(StudioNamesContext)

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisField label={label} help={help} required={required}>
        {names === undefined ? (
          <Input value={text(value)} onChange={(event) => onPick(event.target.value)} />
        ) : (
          <ReferencePicker
            name={label}
            value={text(value)}
            required
            options={referenceOptions(names, kind, undefined, pointer)}
            onChange={(next) => onPick(next ?? '')}
          />
        )}
      </AxisField>
    </div>
  )
}
