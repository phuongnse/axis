import { Button, Card, Checkbox, Flex, Input, Select, Typography } from 'antd'
import { useText } from '../platform/texts'
import { ReferencePicker } from './ReferencePicker'
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
 * The editor of a connector. It shows the base URL binding, the auth type and its bindings, the timeout, the
 * idempotency header and the operations. A binding is only a name: the box neither shows nor writes a value, because
 * values live in tenant settings. The editor edits the draft through `onChange`, so a key it does not show, in the
 * file, in an operation or in `auth`, stays as it is.
 */
export function ConnectorEditor({ value, onChange }: { value: Content; onChange: ChangeHandler }) {
  const t = useText()
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

  function textField(pointer: string, label: string, current: unknown, required: boolean) {
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <div>{label}</div>
        <Input
          aria-label={label}
          value={text(current)}
          onChange={(event) =>
            onChange(pointer, event.target.value === '' && !required ? undefined : event.target.value)
          }
        />
      </div>
    )
  }

  function bindingField(pointer: string, label: string, current: unknown) {
    return (
      <div data-testid={`studio-field-${pointer}`}>
        <div>{label}</div>
        <Input
          aria-label={label}
          value={bindingName(current)}
          onChange={(event) => onChange(pointer, event.target.value)}
        />
        <Typography.Text type="secondary">{t('shell.studio.connector.bindingHint')}</Typography.Text>
      </div>
    )
  }

  return (
    <Flex vertical gap="small" data-testid="studio-connector-editor">
      {textField('/name', t('shell.studio.connector.name'), value.name, true)}
      {bindingField('/baseUrl/binding', t('shell.studio.connector.urlBinding'), value.baseUrl)}
      <div data-testid="studio-field-/auth">
        <div data-testid="studio-field-/auth/type">
          <div>{t('shell.studio.connector.authType')}</div>
          <Select
            virtual={false}
            aria-label={t('shell.studio.connector.authType')}
            style={{ minWidth: 240 }}
            allowClear
            value={authType}
            options={authTypes.map((type) => ({ value: type, label: type }))}
            onChange={(next: string | undefined) => changeAuthType(next)}
          />
        </div>
        {authType === 'bearer' &&
          bindingField('/auth/token/binding', t('shell.studio.connector.secretBinding'), auth?.token)}
        {authType === 'header' && (
          <>
            {textField('/auth/name', t('shell.studio.connector.headerName'), auth?.name, true)}
            {bindingField('/auth/value/binding', t('shell.studio.connector.secretBinding'), auth?.value)}
          </>
        )}
        {authType === 'oauth2' && (
          <>
            {bindingField(
              '/auth/tokenUrl/binding',
              `${t('shell.studio.connector.tokenUrl')} (${t('shell.studio.connector.urlBinding')})`,
              auth?.tokenUrl,
            )}
            {bindingField(
              '/auth/clientId/binding',
              `${t('shell.studio.connector.clientId')} (${t('shell.studio.connector.secretBinding')})`,
              auth?.clientId,
            )}
            {bindingField(
              '/auth/clientSecret/binding',
              `${t('shell.studio.connector.clientSecret')} (${t('shell.studio.connector.secretBinding')})`,
              auth?.clientSecret,
            )}
            {textField('/auth/scope', t('shell.studio.connector.scope'), auth?.scope, false)}
            {textField('/auth/audience', t('shell.studio.connector.audience'), auth?.audience, false)}
          </>
        )}
      </div>
      {textField('/timeout', t('shell.studio.connector.timeout'), value.timeout, false)}
      {textField('/idempotency', t('shell.studio.connector.idempotency'), value.idempotency, false)}
      <div data-testid="studio-field-/operations">
        <Flex vertical gap="small">
          <Typography.Text strong>{t('shell.studio.connector.operations')}</Typography.Text>
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
          <div>
            <Button onClick={() => onChange('/operations', [...operations, { name: '', method: 'POST', path: '/' }])}>
              {t('shell.studio.connector.addOperation')}
            </Button>
          </div>
        </Flex>
      </div>
    </Flex>
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
          <div>{t('shell.studio.connector.operationName')}</div>
          <Input
            aria-label={t('shell.studio.connector.operationName')}
            value={text(operation.name)}
            onChange={(event) => onChange(`${pointer}/name`, event.target.value)}
          />
        </div>
        <div data-testid={`studio-field-${pointer}/method`}>
          <div>{t('shell.studio.connector.method')}</div>
          <Select
            virtual={false}
            aria-label={t('shell.studio.connector.method')}
            style={{ minWidth: 240 }}
            value={typeof operation.method === 'string' ? operation.method : undefined}
            options={methods.map((method) => ({ value: method, label: method }))}
            onChange={(next: string) => onChange(`${pointer}/method`, next)}
          />
        </div>
        <div data-testid={`studio-field-${pointer}/path`}>
          <div>{t('shell.studio.connector.path')}</div>
          <Input
            aria-label={t('shell.studio.connector.path')}
            value={text(operation.path)}
            onChange={(event) => onChange(`${pointer}/path`, event.target.value)}
          />
        </div>
        <div data-testid={`studio-field-${pointer}/lookup`}>
          <div>{t('shell.studio.connector.lookup')}</div>
          <ReferencePicker
            name={t('shell.studio.connector.lookup')}
            value={lookup}
            required={false}
            options={siblings}
            onChange={(next) => onChange(`${pointer}/lookup`, next)}
          />
        </div>
        <FieldRows
          kind="input"
          pointer={`${pointer}/input`}
          label={t('shell.studio.connector.input')}
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
          label={t('shell.studio.connector.output')}
          addText={t('shell.studio.connector.addOutput')}
          rows={operation.output}
          onChange={onChange}
        />
        <div>
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
  label,
  addText,
  rows,
  onChange,
}: {
  kind: 'input' | 'output'
  pointer: string
  label: string
  addText: string
  rows: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const list = Array.isArray(rows) ? rows : []

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Flex vertical gap="small">
        <Typography.Text strong>{label}</Typography.Text>
        {list.map((row, index) => {
          const field = isContent(row) ? row : {}
          const rowPointer = `${pointer}/${index}`
          const structured = kind === 'input' && isStructured(field.type)
          return (
            <Flex key={index} vertical gap="small" data-testid={`studio-field-${rowPointer}`}>
              <Flex gap="small" align="center" wrap>
                <div data-testid={`studio-field-${rowPointer}/name`}>
                  <Input
                    aria-label={`${label} ${t('shell.studio.connector.fieldName')}`}
                    value={text(field.name)}
                    onChange={(event) => onChange(`${rowPointer}/name`, event.target.value)}
                  />
                </div>
                <div data-testid={`studio-field-${rowPointer}/type`}>
                  <Select
                    virtual={false}
                    aria-label={`${label} ${t('shell.studio.connector.fieldType')}`}
                    style={{ minWidth: 140 }}
                    value={typeof field.type === 'string' ? field.type : undefined}
                    options={(kind === 'input' ? inputTypes : fieldTypes).map((type) => ({ value: type, label: type }))}
                    onChange={(next: string) => changeFieldType(onChange, rowPointer, field, next, kind)}
                  />
                </div>
                {kind === 'input' && !structured && (
                  <div data-testid={`studio-field-${rowPointer}/in`}>
                    <Select
                      virtual={false}
                      aria-label={`${label} ${t('shell.studio.connector.placement')}`}
                      style={{ minWidth: 120 }}
                      value={typeof field.in === 'string' ? field.in : 'body'}
                      options={placements.map((placement) => ({ value: placement, label: placement }))}
                      // Body is the default, so choosing it removes the key.
                      onChange={(next: string) => onChange(`${rowPointer}/in`, next === 'body' ? undefined : next)}
                    />
                  </div>
                )}
                <div data-testid={`studio-field-${rowPointer}/required`}>
                  <Checkbox
                    checked={field.required === true}
                    onChange={(event) => onChange(`${rowPointer}/required`, event.target.checked)}
                  >
                    {t('shell.studio.connector.required')}
                  </Checkbox>
                </div>
                {kind === 'output' && (
                  <div data-testid={`studio-field-${rowPointer}/source`}>
                    <Input
                      aria-label={`${label} ${t('shell.studio.connector.source')}`}
                      placeholder={t('shell.studio.connector.source')}
                      value={text(field.source)}
                      onChange={(event) =>
                        onChange(`${rowPointer}/source`, event.target.value === '' ? undefined : event.target.value)
                      }
                    />
                  </div>
                )}
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
          <Button onClick={() => onChange(pointer, [...list, { name: '', type: 'text' }])}>{addText}</Button>
        </div>
      </Flex>
    </div>
  )
}

/** The fields nested in an `object` or `list` input. `level` is the level of these rows, counting the input as 1. */
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

  return (
    <Flex vertical gap="small" style={{ marginInlineStart: 24 }} data-testid={`studio-field-${pointer}`}>
      {list.map((row, index) => {
        const field = isContent(row) ? row : {}
        const rowPointer = `${pointer}/${index}`
        return (
          <Flex key={index} vertical gap="small" data-testid={`studio-field-${rowPointer}`}>
            <Flex gap="small" align="center" wrap>
              <div data-testid={`studio-field-${rowPointer}/name`}>
                <Input
                  aria-label={t('shell.studio.connector.nestedFieldName')}
                  value={text(field.name)}
                  onChange={(event) => onChange(`${rowPointer}/name`, event.target.value)}
                />
              </div>
              <div data-testid={`studio-field-${rowPointer}/type`}>
                <Select
                  virtual={false}
                  aria-label={t('shell.studio.connector.nestedFieldType')}
                  style={{ minWidth: 140 }}
                  value={typeof field.type === 'string' ? field.type : undefined}
                  options={types.map((type) => ({ value: type, label: type }))}
                  onChange={(next: string) => changeFieldType(onChange, rowPointer, field, next, 'nested')}
                />
              </div>
              <div data-testid={`studio-field-${rowPointer}/required`}>
                <Checkbox
                  checked={field.required === true}
                  onChange={(event) => onChange(`${rowPointer}/required`, event.target.checked)}
                >
                  {t('shell.studio.connector.required')}
                </Checkbox>
              </div>
              <Button onClick={() => onChange(rowPointer, undefined)}>{t('shell.studio.connector.removeField')}</Button>
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
        <Button onClick={() => onChange(pointer, [...list, { name: '', type: 'text' }])}>
          {t('shell.studio.connector.addNestedField')}
        </Button>
      </div>
    </Flex>
  )
}
