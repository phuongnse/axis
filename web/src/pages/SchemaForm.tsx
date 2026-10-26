import { Button, Checkbox, Flex, Input, InputNumber, Select, Typography } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import type { StudioNames } from '../platform/studio'
import { parameterType, referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { resolveSchema, type JsonSchema, type ReferenceMark } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { ReferencePicker } from './ReferencePicker'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

/** Called with the JSON Pointer of the value that changed. `undefined` removes the value. */
export type ChangeHandler = (pointer: string, next: unknown) => void

/** Properties that are read-only at the root: the file's identity. */
const readOnlyRootProperties = new Set(['id', 'kind'])

/** The keywords of a conditional. A combination of only these is shown as a form, because the form does not enforce them. */
const conditionalKeywords = new Set(['if', 'then', 'else'])

/**
 * A form built from the JSON Schema of a resource kind, for the draft `value`. It shows text, number,
 * boolean and enum values, nested objects and arrays. A string that names a resource gets a picker of the
 * application's names, as does each key of a map whose keys are marked. Anything else points to the JSON view.
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
      rootValue={value}
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
  rootValue: unknown
  pointer: string
  value: unknown
  onChange: ChangeHandler
}

function Properties({
  node,
  root,
  rootValue,
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
          rootValue={rootValue}
          pointer={`${pointer}/${escapeSegment(name)}`}
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
  rootValue,
  pointer,
  name,
  value,
  required,
  readOnly = false,
  item = false,
  onChange,
}: NodeProps & { name: string; required: boolean; readOnly?: boolean; item?: boolean }) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  const check = useContext(ExpressionCheckContext)
  const label = useContext(LabelTextsContext)
  const schema = textAlternative(resolveSchema(node, root), root, value)
  const reference = node['x-axis-reference'] ?? schema['x-axis-reference']
  const isObject = schema.type === 'object' && schema.properties !== undefined
  const isArray = schema.type === 'array' && schema.items !== undefined
  // A label is an object whose only property is `textKey`. It needs the names and the resource to show its texts.
  const isLabel = isObject && label !== null && names !== undefined && isLabelSchema(schema)
  const keyMark = schema.propertyNames?.['x-axis-reference']
  const isMarkedMap =
    schema.type === 'object' && schema.properties === undefined && keyMark !== undefined && names !== undefined
  const unsupported = !isMarkedMap && !isSupported(schema)

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
        showSearch
        optionFilterProp="label"
        virtual={false}
        style={{ minWidth: 240 }}
        allowClear={!required}
        value={value as string | number | undefined}
        options={schema.enum.map((option) => ({ value: option as string | number, label: String(option) }))}
        onChange={(next) => onChange(pointer, next)}
      />
    )
  } else if (reference !== undefined && schema.type === 'string' && names !== undefined) {
    input = (
      <ReferencePicker
        name={name}
        value={typeof value === 'string' ? value : undefined}
        required={required}
        options={referenceOptions(names, reference, rootValue, pointer)}
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
          options={expressionScope(check.kind, check.draft, pointer, check.names)}
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
  } else if (isMarkedMap) {
    input = (
      <MarkedMap
        schema={schema}
        keyMark={keyMark}
        names={names}
        root={root}
        rootValue={rootValue}
        pointer={pointer}
        name={name}
        value={value}
        onChange={onChange}
      />
    )
  } else if (isLabel) {
    input = (
      <fieldset style={{ margin: 0 }}>
        <legend>{name}</legend>
        <LabelTexts pointer={pointer} value={value} onChange={onChange} />
      </fieldset>
    )
  } else if (isObject) {
    input = (
      <fieldset style={{ margin: 0 }}>
        <legend>{name}</legend>
        <Properties
          node={schema}
          root={root}
          rootValue={rootValue}
          pointer={pointer}
          value={value}
          onChange={onChange}
        />
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
            style={{ border: `1px solid ${colour.border}`, borderRadius: radius.base, padding: spacing.xs }}
          >
            <div style={{ flex: 1 }}>
              <SchemaNode
                node={itemSchema}
                root={root}
                rootValue={rootValue}
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

/**
 * A map whose keys name a part, such as the parameters of a data source. Each row has a picker for the key, a box
 * for the value and Remove. Add appends the first name that no row uses.
 */
function MarkedMap({
  schema,
  keyMark,
  names,
  root,
  rootValue,
  pointer,
  name,
  value,
  onChange,
}: {
  schema: JsonSchema
  keyMark: ReferenceMark
  names: StudioNames
  root: JsonSchema
  rootValue: unknown
  pointer: string
  name: string
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const map = isRecord(value) ? value : {}
  const entries = Object.entries(map)
  const options = (key: string) => referenceOptions(names, keyMark, rootValue, `${pointer}/${escapeSegment(key)}`)
  // A parameter's value follows its type unless the schema gives the values a schema.
  const valueSchema = (key: string): JsonSchema => {
    if (typeof schema.additionalProperties === 'object') {
      return schema.additionalProperties
    }
    const type = parameterType(names, keyMark, rootValue, `${pointer}/${escapeSegment(key)}`, key)
    return {
      type: type === 'integer' ? 'integer' : type === 'decimal' ? 'number' : type === 'boolean' ? 'boolean' : 'string',
    }
  }
  const unused = (key: string) => {
    const used = new Set(entries.map(([other]) => other.toLowerCase()))
    return options(key).filter((option) => !used.has(option.toLowerCase()))
  }
  const next = unused('')[0]

  const rename = (from: string, to: string | undefined) => {
    if (to === undefined || to === '' || to === from) {
      return
    }
    const fresh = defaultFor(valueSchema(to), root)
    onChange(
      pointer,
      Object.fromEntries(
        entries.map(([key, entry]) =>
          key === from ? [to, typeof entry === typeof fresh ? entry : fresh] : [key, entry],
        ),
      ),
    )
  }

  return (
    <Flex vertical gap="small">
      {entries.map(([key, entry], index) => (
        <Flex
          key={key}
          gap="small"
          align="flex-start"
          style={{ border: `1px solid ${colour.border}`, borderRadius: radius.base, padding: spacing.xs }}
        >
          <ReferencePicker
            name={`${name} ${index + 1}`}
            value={key}
            required
            options={options(key).filter(
              (option) => option === key || !entries.some(([other]) => other.toLowerCase() === option.toLowerCase()),
            )}
            onChange={(to) => rename(key, to)}
          />
          <div style={{ flex: 1 }}>
            <SchemaNode
              node={valueSchema(key)}
              root={root}
              rootValue={rootValue}
              pointer={`${pointer}/${escapeSegment(key)}`}
              name={key}
              value={entry}
              required
              item
              onChange={onChange}
            />
          </div>
          <Button onClick={() => onChange(`${pointer}/${escapeSegment(key)}`, undefined)}>
            {t('shell.studio.removeItem')}
          </Button>
        </Flex>
      ))}
      <div>
        <Button
          disabled={next === undefined}
          onClick={() =>
            next !== undefined && onChange(pointer, { ...map, [next]: defaultFor(valueSchema(next), root) })
          }
        >
          {t('shell.studio.addItem')}
        </Button>
      </div>
    </Flex>
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function escapeSegment(segment: string): string {
  return segment.replaceAll('~', '~0').replaceAll('/', '~1')
}

/** Whether the schema is a label: an object whose only property is `textKey`. */
function isLabelSchema(schema: JsonSchema): boolean {
  const keys = Object.keys(schema.properties ?? {})
  return keys.length === 1 && keys[0] === 'textKey'
}

/**
 * The schema the form shows for a node. A node with alternatives whose value is text, or empty, is shown through
 * its first text alternative. An object value keeps the alternatives, so it goes to the JSON view.
 */
function textAlternative(schema: JsonSchema, root: JsonSchema, value: unknown): JsonSchema {
  if (schema.oneOf === undefined || (value !== undefined && typeof value !== 'string')) {
    return schema
  }
  return schema.oneOf.map((option) => resolveSchema(option, root)).find((option) => option.type === 'string') ?? schema
}

/**
 * Whether the form can show a node. Alternatives, maps and nodes without a type go to the JSON view. A combination
 * of only conditionals is shown, because the form does not enforce its conditions.
 */
function isSupported(schema: JsonSchema): boolean {
  if (schema.oneOf !== undefined || schema.anyOf !== undefined) {
    return false
  }
  if (
    schema.allOf !== undefined &&
    !schema.allOf.every((part) => Object.keys(part).every((key) => conditionalKeywords.has(key)))
  ) {
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
  const resolved = resolveSchema(node, root)
  const schema = resolved.oneOf?.[0] === undefined ? resolved : resolveSchema(resolved.oneOf[0], root)
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
