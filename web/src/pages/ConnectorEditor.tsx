import { Button, Card, Checkbox, Flex, Input, Select, Typography } from 'antd'
import { useContext, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { StudioBindingsContext } from '../platform/studioCatalog'
import { useText } from '../platform/texts'
import { BindingSelect } from './CatalogControls'
import { LabelTexts } from './LabelTexts'
import { ReferencePicker } from './ReferencePicker'
import { ResourceFormSection, ResourceFormSections } from './ResourceFormSection'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

const authTypes = ['none', 'bearer', 'header', 'oauth2']
const methods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']
const fieldTypes = ['text', 'integer', 'decimal', 'boolean', 'date', 'date-time']
/** An input may also be an object or a list of nested fields. An output is always a scalar. */
const inputTypes = [...fieldTypes, 'object', 'list']
const placements = ['body', 'path', 'query', 'header']
/** The deepest level of a field that may itself be an object or a list: the input is level 1. */
const nestingLimit = 4

const oauth2Keys = ['tokenUrl', 'clientId', 'clientSecret', 'scope', 'audience']

/** The keys an auth type forbids, and those it requires, from the connector schema. */
const forbiddenAuthKeys: Record<string, string[]> = {
  none: ['token', 'name', 'value', ...oauth2Keys],
  bearer: ['name', 'value', ...oauth2Keys],
  header: ['token', ...oauth2Keys],
  oauth2: ['token', 'name', 'value'],
}

/** The label and the help of a connector field, from `shell.studio.field.connector.<path>`, see docs/reference/frontend.md#platform-field-help. */
function fieldTexts(t: (key: string) => string, path: string) {
  return { label: t(`shell.studio.field.connector.${path}`), help: t(`shell.studio.field.connector.${path}.help`) }
}

/** The title and the help of a connector group of fields, which has no one input to label. */
function groupTexts(t: (key: string) => string, path: string) {
  const { label, help } = fieldTexts(t, path)
  return { title: label, help }
}

function isStructured(type: unknown): boolean {
  return type === 'object' || type === 'list'
}

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** The name of a binding. Only a string name is shown, so nothing else a file holds can appear in the box. */
function bindingName(value: unknown): string {
  return isContent(value) ? text(value.binding) : ''
}

/**
 * The editor of a connector, in the four sections of docs/reference/studio.md#connector-editor: General, Service,
 * Authentication and Operations. It shows the base URL binding, the auth type and its bindings, the timeout, the
 * idempotency header and the operations. A binding is only a name: the field neither shows nor writes a value, because
 * values live in tenant settings. A binding is chosen from the tenant's names of its kind once they load, and is a name box
 * until then. The editor edits the draft through `onChange`, so a key it does not show, in the
 * file, in an operation or in `auth`, stays as it is.
 */
export function ConnectorEditor({ value, onChange }: { value: Content; onChange: ChangeHandler }) {
  const t = useText()
  const bindings = useContext(StudioBindingsContext)
  const auth = isContent(value.auth) ? value.auth : undefined
  const authType = typeof auth?.type === 'string' ? auth.type : undefined
  const operations = Array.isArray(value.operations) ? value.operations : []

  function changeAuthType(next: string | undefined) {
    if (next === undefined) {
      onChange('/auth', undefined)
      return
    }
    const required: Content =
      next === 'bearer'
        ? { token: { binding: '' } }
        : next === 'header'
          ? { name: '', value: { binding: '' } }
          : next === 'oauth2'
            ? { tokenUrl: { binding: '' }, clientId: { binding: '' }, clientSecret: { binding: '' } }
            : {}
    if (auth === undefined) {
      onChange('/auth', { type: next, ...required })
      return
    }
    // Each call stacks on the draft. The keys the new type forbids go, because the schema refuses them.
    onChange('/auth/type', next)
    for (const key of forbiddenAuthKeys[next]) {
      onChange(`/auth/${key}`, undefined)
    }
    for (const [key, initial] of Object.entries(required)) {
      if (auth[key] === undefined) {
        onChange(`/auth/${key}`, initial)
      }
    }
  }

  function textField(pointer: string, path: string, current: unknown, required: boolean) {
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <AxisField {...fieldTexts(t, path)}>
          <Input
            value={text(current)}
            onChange={(event) =>
              onChange(pointer, event.target.value === '' && !required ? undefined : event.target.value)
            }
          />
        </AxisField>
      </div>
    )
  }

  function bindingField(pointer: string, path: string, current: unknown, kind: 'url' | 'secret') {
    const texts = fieldTexts(t, path)
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <AxisField {...texts}>
          {bindings === undefined ? (
            <Input value={bindingName(current)} onChange={(event) => onChange(pointer, event.target.value)} />
          ) : (
            <BindingSelect
              name={texts.label}
              names={bindings[kind]}
              value={bindingName(current)}
              onChange={(next) => onChange(pointer, next)}
            />
          )}
        </AxisField>
      </div>
    )
  }

  function section(name: string, children: ReactNode, action?: ReactNode) {
    const prefix = `shell.studio.section.connector.${name}`
    return (
      <ResourceFormSection testName={name} heading={{ title: t(prefix), line: t(`${prefix}.line`) }} action={action}>
        <Flex vertical gap="small">
          {children}
        </Flex>
      </ResourceFormSection>
    )
  }

  return (
    <div data-testid="studio-connector-editor">
      <ResourceFormSections>
        {section(
          'general',
          <>
            {textField('/name', 'name', value.name, true)}
            <div data-testid="studio-field-/description">
              <AxisFieldGroup {...groupTexts(t, 'description')}>
                <LabelTexts pointer="/description" value={value.description} onChange={onChange} />
              </AxisFieldGroup>
            </div>
          </>,
        )}
        {section(
          'service',
          <>
            {bindingField('/baseUrl/binding', 'baseUrl', value.baseUrl, 'url')}
            {textField('/timeout', 'timeout', value.timeout, false)}
            {textField('/idempotency', 'idempotency', value.idempotency, false)}
          </>,
        )}
        {section(
          'auth',
          <div data-testid="studio-field-/auth">
            <Flex vertical gap="small">
              <div data-testid="studio-field-/auth/type">
                <AxisField {...fieldTexts(t, 'auth.type')}>
                  <Select
                    virtual={false}
                    style={{ minWidth: 240 }}
                    allowClear
                    value={authType}
                    options={authTypes.map((type) => ({ value: type, label: type }))}
                    onChange={(next: string | undefined) => changeAuthType(next)}
                  />
                </AxisField>
              </div>
              {authType === 'bearer' && bindingField('/auth/token/binding', 'auth.token', auth?.token, 'secret')}
              {authType === 'header' && (
                <>
                  {textField('/auth/name', 'auth.name', auth?.name, true)}
                  {bindingField('/auth/value/binding', 'auth.value', auth?.value, 'secret')}
                </>
              )}
              {authType === 'oauth2' && (
                <>
                  {bindingField('/auth/tokenUrl/binding', 'auth.tokenUrl', auth?.tokenUrl, 'url')}
                  {bindingField('/auth/clientId/binding', 'auth.clientId', auth?.clientId, 'secret')}
                  {bindingField('/auth/clientSecret/binding', 'auth.clientSecret', auth?.clientSecret, 'secret')}
                  {textField('/auth/scope', 'auth.scope', auth?.scope, false)}
                  {textField('/auth/audience', 'auth.audience', auth?.audience, false)}
                </>
              )}
            </Flex>
          </div>,
        )}
        {section(
          'operations',
          <div data-testid="studio-field-/operations">
            <Flex vertical gap="small">
              {operations.map((operation, index) => (
                <Operation
                  key={index}
                  index={index}
                  operation={isContent(operation) ? operation : {}}
                  siblings={operations.flatMap((other, position) =>
                    position !== index && isContent(other) && typeof other.name === 'string' && other.name !== ''
                      ? [other.name]
                      : [],
                  )}
                  onChange={onChange}
                />
              ))}
            </Flex>
          </div>,
          <AxisButton
            role="secondary"
            onClick={() => onChange('/operations', [...operations, { name: '', method: 'POST', path: '/' }])}
          >
            {t('shell.studio.connector.addOperation')}
          </AxisButton>,
        )}
      </ResourceFormSections>
    </div>
  )
}

function Operation({
  index,
  operation,
  siblings,
  onChange,
}: {
  index: number
  operation: Content
  siblings: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/operations/${index}`
  const lookup = typeof operation.lookup === 'string' ? operation.lookup : undefined
  const hasBodyInput =
    Array.isArray(operation.input) &&
    operation.input.some((row) => !isContent(row) || row.in === undefined || row.in === 'body')

  return (
    <Card size="small" data-testid={`studio-operation-${index}`}>
      <Flex vertical gap="small">
        <div data-testid={`studio-field-${pointer}/name`}>
          <AxisField {...fieldTexts(t, 'operations.name')}>
            <Input value={text(operation.name)} onChange={(event) => onChange(`${pointer}/name`, event.target.value)} />
          </AxisField>
        </div>
        <div data-testid={`studio-field-${pointer}/method`}>
          <AxisField {...fieldTexts(t, 'operations.method')}>
            <Select
              virtual={false}
              style={{ minWidth: 240 }}
              value={typeof operation.method === 'string' ? operation.method : undefined}
              options={methods.map((method) => ({ value: method, label: method }))}
              onChange={(next: string) => onChange(`${pointer}/method`, next)}
            />
          </AxisField>
        </div>
        <div data-testid={`studio-field-${pointer}/path`}>
          <AxisField {...fieldTexts(t, 'operations.path')}>
            <Input value={text(operation.path)} onChange={(event) => onChange(`${pointer}/path`, event.target.value)} />
          </AxisField>
        </div>
        <div data-testid={`studio-field-${pointer}/lookup`}>
          <AxisField {...fieldTexts(t, 'operations.lookup')}>
            <ReferencePicker
              name={t('shell.studio.field.connector.operations.lookup')}
              value={lookup}
              required={false}
              options={siblings}
              onChange={(next) => onChange(`${pointer}/lookup`, next)}
            />
          </AxisField>
        </div>
        <FieldRows
          kind="input"
          pointer={`${pointer}/input`}
          addText={t('shell.studio.connector.addInput')}
          rows={operation.input}
          onChange={onChange}
        />
        {operation.method === 'GET' && hasBodyInput && (
          <Typography.Text type="warning" data-testid="studio-connector-get-no-input">
            {t('shell.studio.connector.getNoInput')}
          </Typography.Text>
        )}
        <FieldRows
          kind="output"
          pointer={`${pointer}/output`}
          addText={t('shell.studio.connector.addOutput')}
          rows={operation.output}
          onChange={onChange}
        />
        <div>
          {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
          <Button danger onClick={() => onChange(pointer, undefined)}>
            {t('shell.studio.connector.removeOperation')}
          </Button>
        </div>
      </Flex>
    </Card>
  )
}

/** The changes that make a field's `fields` match its new type: the schema needs them on an object or list only. */
function changeFieldType(
  onChange: ChangeHandler,
  pointer: string,
  field: Content,
  next: string,
  kind: 'input' | 'output' | 'nested',
) {
  onChange(`${pointer}/type`, next)
  if (kind === 'output') {
    return
  }
  if (isStructured(next)) {
    // Only the body holds a nested value, so a placement goes.
    if (kind === 'input' && field.in !== undefined) {
      onChange(`${pointer}/in`, undefined)
    }
    if (field.fields === undefined) {
      onChange(`${pointer}/fields`, [{ name: '', type: 'text' }])
    }
  } else if (field.fields !== undefined) {
    onChange(`${pointer}/fields`, undefined)
  }
}

/** The input or output fields of an operation as rows of name, type and required, with a placement or a source. */
function FieldRows({
  kind,
  pointer,
  addText,
  rows,
  onChange,
}: {
  kind: 'input' | 'output'
  pointer: string
  addText: string
  rows: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const list = Array.isArray(rows) ? rows : []
  const path = `operations.${kind}`

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup {...groupTexts(t, path)}>
        <Flex vertical gap="small">
          {list.map((row, index) => {
            const field = isContent(row) ? row : {}
            const rowPointer = `${pointer}/${index}`
            const structured = kind === 'input' && isStructured(field.type)
            return (
              <Flex key={index} vertical gap="small" data-testid={`studio-field-${rowPointer}`}>
                <Flex gap="small" align="flex-end" wrap>
                  <div data-testid={`studio-field-${rowPointer}/name`}>
                    <AxisField {...fieldTexts(t, `${path}.name`)}>
                      <Input
                        value={text(field.name)}
                        onChange={(event) => onChange(`${rowPointer}/name`, event.target.value)}
                      />
                    </AxisField>
                  </div>
                  <div data-testid={`studio-field-${rowPointer}/type`}>
                    <AxisField {...fieldTexts(t, `${path}.type`)}>
                      <Select
                        virtual={false}
                        style={{ minWidth: 140 }}
                        value={typeof field.type === 'string' ? field.type : undefined}
                        options={(kind === 'input' ? inputTypes : fieldTypes).map((type) => ({
                          value: type,
                          label: type,
                        }))}
                        onChange={(next: string) => changeFieldType(onChange, rowPointer, field, next, kind)}
                      />
                    </AxisField>
                  </div>
                  {kind === 'input' && !structured && (
                    <div data-testid={`studio-field-${rowPointer}/in`}>
                      <AxisField {...fieldTexts(t, `${path}.in`)}>
                        <Select
                          virtual={false}
                          style={{ minWidth: 120 }}
                          value={typeof field.in === 'string' ? field.in : 'body'}
                          options={placements.map((placement) => ({ value: placement, label: placement }))}
                          // Body is the default, so choosing it removes the key.
                          onChange={(next: string) => onChange(`${rowPointer}/in`, next === 'body' ? undefined : next)}
                        />
                      </AxisField>
                    </div>
                  )}
                  <div data-testid={`studio-field-${rowPointer}/required`}>
                    <AxisField {...fieldTexts(t, `${path}.required`)}>
                      <Checkbox
                        checked={field.required === true}
                        onChange={(event) => onChange(`${rowPointer}/required`, event.target.checked)}
                      />
                    </AxisField>
                  </div>
                  {kind === 'output' && (
                    <div data-testid={`studio-field-${rowPointer}/source`}>
                      <AxisField {...fieldTexts(t, `${path}.source`)}>
                        <Input
                          value={text(field.source)}
                          onChange={(event) =>
                            onChange(`${rowPointer}/source`, event.target.value === '' ? undefined : event.target.value)
                          }
                        />
                      </AxisField>
                    </div>
                  )}
                  {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                  <Button onClick={() => onChange(rowPointer, undefined)}>
                    {t('shell.studio.connector.removeField')}
                  </Button>
                </Flex>
                {structured && (
                  <NestedFields pointer={`${rowPointer}/fields`} rows={field.fields} level={2} onChange={onChange} />
                )}
              </Flex>
            )
          })}
          <div>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => onChange(pointer, [...list, { name: '', type: 'text' }])}>{addText}</Button>
          </div>
        </Flex>
      </AxisFieldGroup>
    </div>
  )
}

/**
 * The fields nested in an `object` or `list` input. `level` is the level of these rows, counting the input as 1. The
 * schema's nested field is one recursive definition, so every level draws the same field texts.
 */
function NestedFields({
  pointer,
  rows,
  level,
  onChange,
}: {
  pointer: string
  rows: unknown
  level: number
  onChange: ChangeHandler
}) {
  const t = useText()
  const list = Array.isArray(rows) ? rows : []
  const types = level <= nestingLimit ? inputTypes : fieldTypes
  const path = 'operations.input.fields'

  return (
    <div style={{ marginInlineStart: 24 }} data-testid={`studio-field-${pointer}`}>
      <AxisFieldGroup {...groupTexts(t, path)}>
        <Flex vertical gap="small">
          {list.map((row, index) => {
            const field = isContent(row) ? row : {}
            const rowPointer = `${pointer}/${index}`
            return (
              <Flex key={index} vertical gap="small" data-testid={`studio-field-${rowPointer}`}>
                <Flex gap="small" align="flex-end" wrap>
                  <div data-testid={`studio-field-${rowPointer}/name`}>
                    <AxisField {...fieldTexts(t, `${path}.name`)}>
                      <Input
                        value={text(field.name)}
                        onChange={(event) => onChange(`${rowPointer}/name`, event.target.value)}
                      />
                    </AxisField>
                  </div>
                  <div data-testid={`studio-field-${rowPointer}/type`}>
                    <AxisField {...fieldTexts(t, `${path}.type`)}>
                      <Select
                        virtual={false}
                        style={{ minWidth: 140 }}
                        value={typeof field.type === 'string' ? field.type : undefined}
                        options={types.map((type) => ({ value: type, label: type }))}
                        onChange={(next: string) => changeFieldType(onChange, rowPointer, field, next, 'nested')}
                      />
                    </AxisField>
                  </div>
                  <div data-testid={`studio-field-${rowPointer}/required`}>
                    <AxisField {...fieldTexts(t, `${path}.required`)}>
                      <Checkbox
                        checked={field.required === true}
                        onChange={(event) => onChange(`${rowPointer}/required`, event.target.checked)}
                      />
                    </AxisField>
                  </div>
                  {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                  <Button onClick={() => onChange(rowPointer, undefined)}>
                    {t('shell.studio.connector.removeField')}
                  </Button>
                </Flex>
                {isStructured(field.type) && (
                  <NestedFields
                    pointer={`${rowPointer}/fields`}
                    rows={field.fields}
                    level={level + 1}
                    onChange={onChange}
                  />
                )}
              </Flex>
            )
          })}
          <div>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => onChange(pointer, [...list, { name: '', type: 'text' }])}>
              {t('shell.studio.connector.addNestedField')}
            </Button>
          </div>
        </Flex>
      </AxisFieldGroup>
    </div>
  )
}
