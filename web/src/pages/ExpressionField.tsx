import { Input } from 'antd'
import { lazy, Suspense } from 'react'
import type { ExpressionCheckValue } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import type { ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

/**
 * The editor of one expression, the one way every place draws it: the expression editor when `check` says where the
 * draft is checked, a plain box otherwise. The `id`, `aria-describedby`, `aria-required`, `aria-invalid` and `status` that
 * `AxisField` gives its input reach the plain boxes only, as the expression editor does not take them yet. It keeps its
 * own name.
 */
export function ExpressionField({
  check,
  pointer,
  name,
  value,
  required,
  onChange,
  id,
  'aria-describedby': describedBy,
  'aria-required': ariaRequired,
  'aria-invalid': ariaInvalid,
  status,
}: {
  check: ExpressionCheckValue | null
  pointer: string
  name: string
  value: string
  required: boolean
  onChange: ChangeHandler
  id?: string
  'aria-describedby'?: string
  'aria-required'?: boolean
  'aria-invalid'?: boolean
  status?: 'error'
}) {
  if (check === null) {
    return (
      <Input
        aria-label={name}
        id={id}
        aria-describedby={describedBy}
        aria-required={ariaRequired}
        aria-invalid={ariaInvalid}
        status={status}
        value={value}
        onChange={(event) => onChange(pointer, event.target.value === '' && !required ? undefined : event.target.value)}
      />
    )
  }
  return (
    <Suspense
      fallback={
        <Input
          aria-label={name}
          id={id}
          aria-describedby={describedBy}
          aria-required={ariaRequired}
          aria-invalid={ariaInvalid}
          status={status}
          value={value}
          readOnly
          aria-busy="true"
        />
      }
    >
      <LazyExpressionEditor
        application={check.application}
        resourceId={check.resourceId}
        path={pointer}
        name={name}
        value={value}
        required={required}
        draft={check.draft}
        options={() => expressionScope(check.kind, check.draft(), pointer, check.names)}
        names={check.names}
        onChange={onChange}
      />
    </Suspense>
  )
}
