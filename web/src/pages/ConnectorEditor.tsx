import { Button, Card, Checkbox, Flex, Input, Select, Typography } from 'antd'
import { useText } from '../platform/texts'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

const authTypes = ['none', 'bearer', 'header']
const methods = ['GET', 'POST', 'PUT', 'PATCH', 'DELETE']
const fieldTypes = ['text', 'integer', 'decimal', 'boolean', 'date', 'date-time']

/** The keys an auth type forbids, and those it requires, from the connector schema. */
const forbiddenAuthKeys: Record<string, string[]> = {
  none: ['token', 'name', 'value'],
  bearer: ['name', 'value'],
  header: ['token'],
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
 * The editor of a connector. It shows the base URL binding, the auth type and its secret binding, the timeout, the
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
      next === 'bearer' ? { token: { binding: '' } } : next === 'header' ? { name: '', value: { binding: '' } } : {}
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
  const hasInput = Array.isArray(operation.input) && operation.input.length > 0

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
          pointer={`${pointer}/input`}
          label={t('shell.studio.connector.input')}
          addText={t('shell.studio.connector.addInput')}
          rows={operation.input}
          onChange={onChange}
        />
        {operation.method === 'GET' && hasInput && (
          <Typography.Text type="warning" data-testid="studio-connector-get-no-input">
            {t('shell.studio.connector.getNoInput')}
          </Typography.Text>
        )}
        <FieldRows
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

/** The input or output fields of an operation as rows of name, type and required. */
function FieldRows({
  pointer,
  label,
  addText,
  rows,
  onChange,
}: {
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
          return (
            <Flex key={index} gap="small" align="center" wrap data-testid={`studio-field-${rowPointer}`}>
              <div data-testid={`studio-field-${rowPointer}/name`}>
                <Input
                  aria-label={`${label} ${t('shell.studio.connector.fieldName')}`}
                  value={text(field.name)}
                  onChange={(event) => onChange(`${rowPointer}/name`, event.target.value)}
                />
              </div>
              <div data-testid={`studio-field-${rowPointer}/type`}>
                <Select
                  aria-label={`${label} ${t('shell.studio.connector.fieldType')}`}
                  style={{ minWidth: 140 }}
                  value={typeof field.type === 'string' ? field.type : undefined}
                  options={fieldTypes.map((type) => ({ value: type, label: type }))}
                  onChange={(next: string) => onChange(`${rowPointer}/type`, next)}
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
          )
        })}
        <div>
          <Button onClick={() => onChange(pointer, [...list, { name: '', type: 'text' }])}>{addText}</Button>
        </div>
      </Flex>
    </div>
  )
}
