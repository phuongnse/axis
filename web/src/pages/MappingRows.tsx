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

/** What a row maps its key to: an expression, or a field picked from `options`, or typed while they are unknown. */
export type MappingTarget = { kind: 'expression' } | { kind: 'field'; label: string; options?: string[] }

/**
 * A map of the draft as rows of a key and its target, at `pointer`. A row's key is picked from `keyOptions`, or typed
 * while they are unknown. The rows edit the draft through `onChange`: a rename keeps the row's place in the file, and
 * a value that is not an object is left to the JSON view, and so is a row whose entry is an object.
 */
export function MappingRows({
  pointer,
  title,
  value,
  keyLabel,
  keyOptions,
  target,
  onChange,
}: {
  pointer: string
  title: string
  value: unknown
  keyLabel: string
  keyOptions?: string[]
  target: MappingTarget
  onChange: ChangeHandler
}) {
  const t = useText()
  const check = useContext(ExpressionCheckContext)

  if (value !== undefined && (typeof value !== 'object' || value === null || Array.isArray(value))) {
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <div>{title}</div>
        <Typography.Text type="secondary" data-testid="studio-json-only">
          {t('shell.studio.editInJson')}
        </Typography.Text>
      </div>
    )
  }

  const entries = Object.entries((value ?? {}) as Record<string, unknown>)
  const unmapped = (keyOptions ?? []).filter(
    (option) => !entries.some(([key]) => key.toLowerCase() === option.toLowerCase()),
  )
  const expressionName = t('shell.studio.process.expression')

  function rename(from: string, to: string) {
    if (to === from || entries.some(([key]) => key === to)) {
      return
    }
    onChange(pointer, Object.fromEntries(entries.map(([key, entry]) => [key === from ? to : key, entry])))
  }

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{title}</Typography.Text>
        {entries.map(([key, entry], index) => {
          const rowPointer = `${pointer}/${escapeSegment(key)}`
          const text = typeof entry === 'string' ? entry : ''
          return (
            <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${rowPointer}`}>
              <div>
                {keyOptions === undefined ? (
                  <Input aria-label={keyLabel} value={key} onChange={(event) => rename(key, event.target.value)} />
                ) : (
                  <ReferencePicker
                    name={keyLabel}
                    value={key}
                    required
                    options={keyOptions}
                    onChange={(next) => rename(key, next ?? '')}
                  />
                )}
              </div>
              {typeof entry === 'object' && entry !== null ? (
                <Typography.Text type="secondary" data-testid="studio-json-only" style={{ flex: 1, minWidth: 240 }}>
                  {t('shell.studio.editInJson')}
                </Typography.Text>
              ) : (
                <div style={{ flex: 1, minWidth: 240 }}>
                  {target.kind === 'field' ? (
                    target.options === undefined ? (
                      <Input
                        aria-label={target.label}
                        value={text}
                        onChange={(event) => onChange(rowPointer, event.target.value)}
                      />
                    ) : (
                      <ReferencePicker
                        name={target.label}
                        value={text}
                        required
                        options={target.options}
                        onChange={(next) => onChange(rowPointer, next ?? '')}
                      />
                    )
                  ) : check ? (
                    <Suspense fallback={<Input aria-label={expressionName} value={text} readOnly />}>
                      <LazyExpressionEditor
                        application={check.application}
                        resourceId={check.resourceId}
                        path={rowPointer}
                        name={expressionName}
                        value={text}
                        required
                        options={expressionScope(check.kind, check.draft, rowPointer, check.names)}
                        onChange={onChange}
                      />
                    </Suspense>
                  ) : (
                    <Input
                      aria-label={expressionName}
                      value={text}
                      onChange={(event) => onChange(rowPointer, event.target.value)}
                    />
                  )}
                </div>
              )}
              <Button onClick={() => onChange(rowPointer, undefined)}>{t('shell.studio.process.removeMapping')}</Button>
            </Flex>
          )
        })}
        <div>
          {keyOptions === undefined ? (
            <Button disabled={entries.some(([key]) => key === '')} onClick={() => onChange(`${pointer}/`, '')}>
              {t('shell.studio.process.addMapping')}
            </Button>
          ) : (
            <Select
              aria-label={`${t('shell.studio.process.addMapping')} ${title}`}
              placeholder={t('shell.studio.process.addMapping')}
              style={{ minWidth: 240 }}
              showSearch
              virtual={false}
              value={null}
              options={unmapped.map((option) => ({ value: option, label: option }))}
              onChange={(next: string) => onChange(`${pointer}/${escapeSegment(next)}`, '')}
            />
          )}
        </div>
      </Flex>
    </div>
  )
}
