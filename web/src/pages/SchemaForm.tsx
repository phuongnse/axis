import { Button, Checkbox, Flex, Input, InputNumber, Select, Typography } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import { resolveSchema, type JsonSchema } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

/** Called with the JSON Pointer of the value that changed. `undefined` removes the value. */
export type ChangeHandler = (pointer: string, next: unknown) => void

/** Properties that are read-only at the root: the file's identity. */
const readOnlyRootProperties = new Set(['id', 'kind'])

/**
 * A form built from the JSON Schema of a resource kind, for the draft `value`. It shows text, number,
 * boolean and enum values, nested objects and arrays. Anything else points to the JSON view.
 */
export function SchemaForm({
  schema,
  value,
  onChange,
}: {
  schema: JsonSchema
  value: Record<string, unknown>
  onChange: ChangeHandler
}) {
  return (
    <Properties
      node={schema}
      root={schema}
      pointer=""
      value={value}
      onChange={onChange}
      readOnlyNames={readOnlyRootProperties}
    />
  )
}

interface NodeProps {
  node: JsonSchema
  root: JsonSchema
  pointer: string
  value: unknown
  onChange: ChangeHandler
}

function Properties({
  node,
  root,
  pointer,
  value,
  onChange,
  readOnlyNames,
}: NodeProps & { readOnlyNames?: Set<string> }) {
  const required = new Set(node.required)
  return (
    <Flex vertical gap="small">
      {Object.entries(node.properties ?? {}).map(([name, child]) => (
        <SchemaNode
          key={name}
          node={child}
          root={root}
          pointer={`${pointer}/${name.replaceAll('~', '~0').replaceAll('/', '~1')}`}
          name={name}
          value={(value as Record<string, unknown> | undefined)?.[name]}
          required={required.has(name)}
          readOnly={readOnlyNames?.has(name) ?? false}
          onChange={onChange}
        />
      ))}
    </Flex>
  )
}

function SchemaNode({
  node,
  root,
  pointer,
  name,
  value,
  required,
  readOnly = false,
  item = false,
  onChange,
}: NodeProps & { name: string; required: boolean; readOnly?: boolean; item?: boolean }) {
  const t = useText()
  const check = useContext(ExpressionCheckContext)
  const schema = resolveSchema(node, root)
  const isObject = schema.type === 'object' && schema.properties !== undefined
  const isArray = schema.type === 'array' && schema.items !== undefined
  const unsupported = !isSupported(schema)

  let input
  if (unsupported) {
    input = (
      <Typography.Text type="secondary" data-testid="studio-json-only">
        {t('shell.studio.editInJson')}
      </Typography.Text>
    )
  } else if (schema.const !== undefined || readOnly) {
    input = <Typography.Text>{String(schema.const ?? value ?? '')}</Typography.Text>
  } else if (schema.enum !== undefined) {
    input = (
      <Select
        aria-label={name}
        style={{ minWidth: 240 }}
        allowClear={!required}
        value={value as string | number | undefined}
        options={schema.enum.map((option) => ({ value: option as string | number, label: String(option) }))}
        onChange={(next) => onChange(pointer, next)}
      />
    )
  } else if (schema.type === 'string' && schema['x-axis-expression'] === true && check) {
    const text = typeof value === 'string' ? value : ''
    input = (
      <Suspense fallback={<Input aria-label={name} value={text} readOnly />}>
        <LazyExpressionEditor
          application={check.application}
          resourceId={check.resourceId}
          path={pointer}
          name={name}
          value={text}
          required={required}
          onChange={onChange}
        />
      </Suspense>
    )
  } else if (schema.type === 'string') {
    input = (
      <Input
        aria-label={name}
        value={typeof value === 'string' ? value : ''}
        onChange={(event) => onChange(pointer, event.target.value === '' && !required ? undefined : event.target.value)}
      />
    )
  } else if (schema.type === 'integer' || schema.type === 'number') {
    input = (
      <InputNumber
        aria-label={name}
        precision={schema.type === 'integer' ? 0 : undefined}
        min={schema.minimum}
        max={schema.maximum}
        value={typeof value === 'number' ? value : null}
        onChange={(next) => {
          // An empty array item stays, because clearing it would remove the item.
          if (typeof next === 'number' || !item) {
            onChange(pointer, typeof next === 'number' ? next : undefined)
          }
        }}
      />
    )
  } else if (schema.type === 'boolean') {
    input = (
      <Checkbox
        aria-label={name}
        checked={value === true}
        onChange={(event) => onChange(pointer, event.target.checked)}
      />
    )
  } else if (isObject) {
    input = (
      <fieldset style={{ margin: 0 }}>
        <legend>{name}</legend>
        <Properties node={schema} root={root} pointer={pointer} value={value} onChange={onChange} />
      </fieldset>
    )
  } else if (isArray) {
    const items = Array.isArray(value) ? value : []
    const itemSchema = schema.items!
    input = (
      <Flex vertical gap="small">
        {items.map((item, index) => (
          <Flex
            key={index}
            gap="small"
            align="flex-start"
            style={{ border: '1px solid #d9d9d9', borderRadius: 6, padding: 8 }}
          >
            <div style={{ flex: 1 }}>
              <SchemaNode
                node={itemSchema}
                root={root}
                pointer={`${pointer}/${index}`}
                name={`${name} ${index + 1}`}
                value={item}
                required
                item
                onChange={onChange}
              />
            </div>
            <Button onClick={() => onChange(`${pointer}/${index}`, undefined)}>{t('shell.studio.removeItem')}</Button>
          </Flex>
        ))}
        <div>
          <Button onClick={() => onChange(pointer, [...items, defaultFor(itemSchema, root)])}>
            {t('shell.studio.addItem')}
          </Button>
        </div>
      </Flex>
    )
  }

  const showsLabel = !isObject
  return (
    <div data-testid={`studio-field-${pointer}`}>
      {showsLabel && <div>{name}</div>}
      {input}
    </div>
  )
}

/** Whether the form can show a node. Combinations, maps and nodes without a type go to the JSON view. */
function isSupported(schema: JsonSchema): boolean {
  if (schema.oneOf !== undefined || schema.anyOf !== undefined || schema.allOf !== undefined) {
    return false
  }
  if (schema.const !== undefined || schema.enum !== undefined) {
    return true
  }
  switch (schema.type) {
    case 'object':
      return schema.properties !== undefined
    case 'array':
      return schema.items !== undefined
    case undefined:
      return false
    default:
      return true
  }
}

/** The value Add appends to an array: an empty value of the item's type, or its first enum value. */
function defaultFor(node: JsonSchema, root: JsonSchema): unknown {
  const schema = resolveSchema(node, root)
  if (schema.enum !== undefined) {
    return schema.enum[0]
  }
  switch (schema.type) {
    case 'object':
      return {}
    case 'array':
      return []
    case 'integer':
    case 'number':
      return 0
    case 'boolean':
      return false
    default:
      return ''
  }
}
