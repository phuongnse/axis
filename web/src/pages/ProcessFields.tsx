import { Input } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** The editor of an expression: the expression editor when the draft can check expressions, a plain box otherwise. */
export function Expression({
  pointer,
  name,
  value,
  onChange,
}: {
  pointer: string
  name: string
  value: string
  onChange: ChangeHandler
}) {
  const check = useContext(ExpressionCheckContext)

  return check ? (
    <Suspense fallback={<Input aria-label={name} value={value} readOnly />}>
      <LazyExpressionEditor
        application={check.application}
        resourceId={check.resourceId}
        path={pointer}
        name={name}
        value={value}
        required
        options={expressionScope(check.kind, check.draft, pointer, check.names)}
        onChange={onChange}
      />
    </Suspense>
  ) : (
    <Input aria-label={name} value={value} onChange={(event) => onChange(pointer, event.target.value)} />
  )
}

/** A picker over the application's names of one kind, or a plain box while the names have not loaded. */
export function NamePicker({
  pointer,
  label,
  kind,
  value,
  onPick,
}: {
  pointer: string
  label: string
  kind: string
  value: unknown
  onPick: (next: string) => void
}) {
  const names = useContext(StudioNamesContext)

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{label}</div>
      {names === undefined ? (
        <Input aria-label={label} value={text(value)} onChange={(event) => onPick(event.target.value)} />
      ) : (
        <ReferencePicker
          name={label}
          value={text(value)}
          required
          options={referenceOptions(names, kind, undefined, pointer)}
          onChange={(next) => onPick(next ?? '')}
        />
      )}
    </div>
  )
}
