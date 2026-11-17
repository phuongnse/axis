import { Button, Flex, Input, Typography } from 'antd'
import { useContext } from 'react'
import { AxisChoiceField } from '../platform/AxisChoiceField'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { useFieldTexts } from './fieldTexts'
import { Expression } from './ProcessFields'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

const kinds = ['value', 'object', 'list'] as const

type Kind = (typeof kinds)[number]

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function escapeSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

/** An entry is a value when it is text or missing, a list when it has a `from`, and an object when it is any other object. */
function kindOf(entry: unknown): Kind | undefined {
  if (entry === undefined || typeof entry === 'string') {
    return 'value'
  }
  if (!isContent(entry)) {
    return undefined
  }
  return 'from' in entry ? 'list' : 'object'
}

/**
 * The inputs of a connector call as rows, at `pointer`: an object of input names, or the `fields` of an `object` or
 * `list` input. A row has a name, a Kind choice of a value, an object and a list, and each field sits on its own line.
 * A value is an expression, an object is nested rows over the same record, and a list picks a child collection of
 * `entity` with `from` and has nested rows over a row of it. Nested rows use the same fields and nest further. The step
 * keeps the shape the author switched to, and the compiler reports what is missing. A value that is not a plain object
 * is left to the JSON view.
 */
export function ConnectorInputRows({
  pointer,
  title,
  help,
  value,
  entity,
  onChange,
}: {
  pointer: string
  title: string
  help: string
  value: unknown
  entity: string
  onChange: ChangeHandler
}) {
  const t = useText()
  const { field, group } = useFieldTexts('process')

  if (value !== undefined && !isContent(value)) {
    return <JsonOnly pointer={pointer} title={title} help={help} />
  }

  const entries = Object.entries(value ?? {})
  const expressionField = field('steps.input.value')

  function rename(from: string, to: string) {
    if (to === from || entries.some(([key]) => key === to)) {
      return
    }
    onChange(pointer, Object.fromEntries(entries.map(([key, entry]) => [key === from ? to : key, entry])))
  }

  function switchTo(rowPointer: string, entry: unknown, next: Kind) {
    const content = isContent(entry) ? entry : {}
    if (next === 'value') {
      onChange(rowPointer, '')
    } else if (next === 'object') {
      onChange(rowPointer, { fields: content.fields ?? {} })
    } else {
      onChange(rowPointer, { from: text(content.from), fields: content.fields ?? {} })
    }
  }

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup title={title} help={help}>
        <Flex vertical gap="small">
          {entries.map(([key, entry], index) => {
            const rowPointer = `${pointer}/${escapeSegment(key)}`
            const kind = kindOf(entry)
            return (
              <Flex key={index} vertical gap="small" data-testid={`studio-field-${rowPointer}`}>
                <AxisField {...field('steps.input.name')} required>
                  <Input value={key} onChange={(event) => rename(key, event.target.value)} />
                </AxisField>
                <AxisChoiceField
                  {...field('steps.input.kind')}
                  required
                  value={kind}
                  options={kinds.map((candidate) => ({ value: candidate, ...field(`steps.input.kind.${candidate}`) }))}
                  onChange={(next) => {
                    if (next !== kind) {
                      switchTo(rowPointer, entry, next as Kind)
                    }
                  }}
                />
                {kind === 'value' && (
                  <AxisField {...expressionField} required>
                    <Expression
                      pointer={rowPointer}
                      name={expressionField.label}
                      value={text(entry)}
                      onChange={onChange}
                    />
                  </AxisField>
                )}
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                <Button onClick={() => onChange(rowPointer, undefined)}>
                  {t('shell.studio.process.removeMapping')}
                </Button>
                {kind === undefined && (
                  <Typography.Text type="secondary" data-testid="studio-json-only">
                    {t('shell.studio.editInJson')}
                  </Typography.Text>
                )}
                {kind === 'object' && (
                  <div style={{ marginInlineStart: 24 }}>
                    <ConnectorInputRows
                      pointer={`${rowPointer}/fields`}
                      {...group('steps.input.fields')}
                      value={(entry as Content).fields}
                      entity={entity}
                      onChange={onChange}
                    />
                  </div>
                )}
                {kind === 'list' && (
                  <ListRows pointer={rowPointer} value={entry as Content} entity={entity} onChange={onChange} />
                )}
              </Flex>
            )
          })}
          <div>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button disabled={entries.some(([key]) => key === '')} onClick={() => onChange(`${pointer}/`, '')}>
              {t('shell.studio.process.addMapping')}
            </Button>
          </div>
        </Flex>
      </AxisFieldGroup>
    </div>
  )
}

/** The `from` of a list input, a picker of the child collections of `entity`, and its nested rows over a child row. */
function ListRows({
  pointer,
  value,
  entity,
  onChange,
}: {
  pointer: string
  value: Content
  entity: string
  onChange: ChangeHandler
}) {
  const { field, group } = useFieldTexts('process')
  const names = useContext(ExpressionCheckContext)?.names ?? null
  const from = field('steps.input.from')
  const fromValue = text(value.from)
  const collections =
    names?.entities
      .find((item) => item.name.toLowerCase() === entity.toLowerCase())
      ?.fields.filter((item) => item.type === 'child-collection') ?? []
  const picked = collections.find((item) => item.name.toLowerCase() === fromValue.toLowerCase())

  return (
    <Flex vertical gap="small" style={{ marginInlineStart: 24 }}>
      <div data-testid={`studio-field-${pointer}/from`}>
        <AxisField {...from}>
          {names === null ? (
            <Input value={fromValue} onChange={(event) => onChange(`${pointer}/from`, event.target.value)} />
          ) : (
            <ReferencePicker
              name={from.label}
              value={fromValue}
              required
              options={collections}
              onChange={(next) => onChange(`${pointer}/from`, next ?? '')}
            />
          )}
        </AxisField>
      </div>
      <ConnectorInputRows
        pointer={`${pointer}/fields`}
        {...group('steps.input.fields')}
        value={value.fields}
        entity={picked?.target ?? ''}
        onChange={onChange}
      />
    </Flex>
  )
}

function JsonOnly({ pointer, title, help }: { pointer: string; title: string; help: string }) {
  const t = useText()
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup title={title} help={help}>
        <Typography.Text type="secondary" data-testid="studio-json-only">
          {t('shell.studio.editInJson')}
        </Typography.Text>
      </AxisFieldGroup>
    </div>
  )
}
