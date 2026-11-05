import { Input } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionExpectation, expressionScope } from './expressionScope'
import type { ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

/**
 * An expression at `pointer` of the resource being edited: the expression editor, with the names, the functions and
 * the type that place has, inside the expression check of a Studio editor, else a plain box. Read-only, it is a plain
 * box too. Proposed: the one wrapper the entity designer, the process editor, notifications and connector inputs share,
 * in place of two copies.
 */
export function Expression({
  pointer,
  name,
  value,
  required = true,
  readOnly = false,
  onChange,
}: {
  pointer: string
  name: string
  value: string
  required?: boolean
  readOnly?: boolean
  onChange: ChangeHandler
}) {
  const check = useContext(ExpressionCheckContext)

  return check && !readOnly ? (
    <Suspense fallback={<Input aria-label={name} value={value} readOnly />}>
      <LazyExpressionEditor
        application={check.application}
        resourceId={check.resourceId}
        path={pointer}
        name={name}
        value={value}
        required={required}
        options={expressionScope(check.kind, check.draft, pointer, check.names)}
        expects={expressionExpectation(check.kind, check.draft, pointer)}
        onChange={onChange}
      />
    </Suspense>
  ) : (
    <Input
      aria-label={name}
      value={value}
      readOnly={readOnly}
      onChange={(event) => onChange(pointer, event.target.value === '' && !required ? undefined : event.target.value)}
    />
  )
}
