import { Button, Flex, Input, Select, Typography } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

function escapeSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

/**
 * The `values` of an endpoint as rows of a field of its entity and the expression that gives the field its value. A
 * row's field is picked from `fields`, or typed while they are unknown. The rows edit the draft through `onChange`: a
 * rename keeps the row's place in the file, and a value that is not an object is left to the JSON view.
 */
export function EndpointValues({
  fields,
  values,
  onChange,
}: {
  fields: string[] | undefined
  values: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const check = useContext(ExpressionCheckContext)

  if (values !== undefined && (typeof values !== 'object' || values === null || Array.isArray(values))) {
    return (
      <div data-testid="studio-field-/values">
        <div>{t('shell.studio.endpoint.values')}</div>
        <Typography.Text type="secondary" data-testid="studio-json-only">
          {t('shell.studio.editInJson')}
        </Typography.Text>
      </div>
    )
  }

  const entries = Object.entries((values ?? {}) as Record<string, unknown>)
  const unmapped = (fields ?? []).filter((field) => !entries.some(([key]) => key.toLowerCase() === field.toLowerCase()))

  function rename(from: string, to: string) {
    if (to === from || entries.some(([key]) => key === to)) {
      return
    }
    onChange('/values', Object.fromEntries(entries.map(([key, value]) => [key === from ? to : key, value])))
  }

  return (
    <div data-testid="studio-field-/values">
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.endpoint.values')}</Typography.Text>
        {entries.map(([key, expression], index) => {
          const pointer = `/values/${escapeSegment(key)}`
          const text = typeof expression === 'string' ? expression : ''
          return (
            <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${pointer}`}>
              <div>
                {fields === undefined ? (
                  <Input
                    aria-label={t('shell.studio.endpoint.field')}
                    value={key}
                    onChange={(event) => rename(key, event.target.value)}
                  />
                ) : (
                  <ReferencePicker
                    name={t('shell.studio.endpoint.field')}
                    value={key}
                    required
                    options={fields}
                    onChange={(next) => rename(key, next ?? '')}
                  />
                )}
              </div>
              <div style={{ flex: 1, minWidth: 240 }}>
                {check ? (
                  <Suspense
                    fallback={<Input aria-label={t('shell.studio.endpoint.expression')} value={text} readOnly />}
                  >
                    <LazyExpressionEditor
                      application={check.application}
                      resourceId={check.resourceId}
                      path={pointer}
                      name={t('shell.studio.endpoint.expression')}
                      value={text}
                      required
                      options={expressionScope(check.kind, check.draft, pointer, check.names)}
                      onChange={onChange}
                    />
                  </Suspense>
                ) : (
                  <Input
                    aria-label={t('shell.studio.endpoint.expression')}
                    value={text}
                    onChange={(event) => onChange(pointer, event.target.value)}
                  />
                )}
              </div>
              <Button onClick={() => onChange(pointer, undefined)}>{t('shell.studio.endpoint.removeMapping')}</Button>
            </Flex>
          )
        })}
        <div>
          {fields === undefined ? (
            <Button disabled={entries.some(([key]) => key === '')} onClick={() => onChange('/values/', '')}>
              {t('shell.studio.endpoint.addMapping')}
            </Button>
          ) : (
            <Select
              aria-label={t('shell.studio.endpoint.addMapping')}
              placeholder={t('shell.studio.endpoint.addMapping')}
              style={{ minWidth: 240 }}
              showSearch
              virtual={false}
              value={null}
              options={unmapped.map((field) => ({ value: field, label: field }))}
              onChange={(next: string) => onChange(`/values/${escapeSegment(next)}`, '')}
            />
          )}
        </div>
      </Flex>
    </div>
  )
}
