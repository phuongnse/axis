import { Input } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import { AxisField } from '../platform/AxisField'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/**
 * The editor of an expression: the expression editor when the draft can check expressions, a plain box otherwise. The
 * `id` and `aria-describedby` that `AxisField` gives its input reach the plain box only, as the expression editor does
 * not take them yet. It keeps its own name. A `readOnly` expression is always the plain box.
 */
export function Expression({
  pointer,
  name,
  id,
  'aria-describedby': describedBy,
  value,
  required = true,
  readOnly = false,
  onChange,
}: {
  pointer: string
  name: string
  id?: string
  'aria-describedby'?: string
  value: string
  required?: boolean
  readOnly?: boolean
  onChange: ChangeHandler
}) {
  const check = useContext(ExpressionCheckContext)

  return check && !readOnly ? (
    <Suspense
      fallback={
        <Input aria-label={name} id={id} aria-describedby={describedBy} value={value} readOnly aria-busy="true" />
      }
    >
      <LazyExpressionEditor
        application={check.application}
        resourceId={check.resourceId}
        path={pointer}
        name={name}
        value={value}
        required={required}
        options={expressionScope(check.kind, check.draft, pointer, check.names)}
        onChange={onChange}
      />
    </Suspense>
  ) : (
    <Input
      aria-label={name}
      id={id}
      aria-describedby={describedBy}
      value={value}
      onChange={(event) => onChange(pointer, event.target.value === '' && !required ? undefined : event.target.value)}
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
  value,
  onPick,
}: {
  pointer: string
  label: string
  help: string
  kind: string
  value: unknown
  onPick: (next: string) => void
}) {
  const names = useContext(StudioNamesContext)

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisField label={label} help={help}>
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
