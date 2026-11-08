import { Button, Checkbox, Flex, Input, InputNumber, Select, Typography } from 'antd'
import { lazy, Suspense, useContext, useId, type ReactNode } from 'react'
import type { StudioNames } from '../platform/studio'
import { keyPropertyOf, type KeyedLists } from '../platform/layerMarks'
import { parameterType, referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { resolveSchema, type JsonSchema, type ReferenceMark } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { AxisButton } from '../platform/AxisButton'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { LayerMarkTag, RemovedRows, type DesignerLayer } from './LayerMarkViews'
import { ReferencePicker } from './ReferencePicker'
import { SchemaLayerContext } from './schemaLayerContext'
import { sectionsByKind } from './schemaFormSections'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

/** Called with the JSON Pointer of the value that changed. `undefined` removes the value. */
export type ChangeHandler = (pointer: string, next: unknown) => void

/** The file's identity, which the root form never shows, see docs/reference/studio-resource-form.md#fields. */
const hiddenRootProperties = new Set(['id', 'kind', 'formatVersion'])

/** The keywords of a conditional. A combination of only these is shown as a form, because the form does not enforce them. */
const conditionalKeywords = new Set(['if', 'then', 'else'])

/**
 * A form built from the JSON Schema of a resource kind, for the draft `value`. It shows text, number,
 * boolean and enum values, nested objects and arrays. A string that names a resource gets a picker of the
 * application's names, as does each key of a map whose keys are marked. Anything else points to the JSON view.
 * Each root property is named by its field text, and the file's identity is hidden. `readOnlyNames` lists the root
 * properties that show as text instead of an input. With a `layer` and its `keyedLists`, each element of a keyed list
 * shows its Added or Changed tag with Reset to module, and the elements the layer removed show as muted rows below the list.
 */
export function SchemaForm({
  schema,
  value,
  onChange,
  layer,
  keyedLists,
  readOnlyNames,
}: {
  schema: JsonSchema
  value: Record<string, unknown>
  onChange: ChangeHandler
  layer?: DesignerLayer
  keyedLists?: KeyedLists
  readOnlyNames?: Set<string>
}) {
  return (
    <SchemaLayerContext.Provider value={layer && keyedLists && { designer: layer, keyedLists }}>
      <Sections schema={schema} value={value} onChange={onChange} readOnlyNames={readOnlyNames} />
    </SchemaLayerContext.Provider>
  )
}

interface NodeProps {
  node: JsonSchema
  root: JsonSchema
  rootValue: unknown
  pointer: string
  /** The property names from the root to this node, joined by dots, with list indices and map keys left out. */
  path: string
  value: unknown
  onChange: ChangeHandler
}

/**
 * The root properties in sections, see docs/reference/studio-resource-form.md#sections. A property that no section
 * names goes to a last section with no heading.
 */
function Sections({
  schema,
  value,
  onChange,
  readOnlyNames,
}: {
  schema: JsonSchema
  value: Record<string, unknown>
  onChange: ChangeHandler
  readOnlyNames?: Set<string>
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const headingId = useId()
  const kind = schema.properties?.kind?.const
  const shown = Object.keys(schema.properties ?? {}).filter((name) => !hiddenRootProperties.has(name))
  const mapped = typeof kind === 'string' && Object.hasOwn(sectionsByKind, kind) ? sectionsByKind[kind] : []
  const named = new Set(mapped.flatMap((section) => section.properties))
  const groups = [
    ...mapped.map((section) => ({
      name: section.name as string | undefined,
      properties: section.properties.filter((name) => shown.includes(name)),
    })),
    { name: undefined, properties: shown.filter((name) => !named.has(name)) },
  ].filter((group) => group.properties.length > 0)
  const panel = {
    background: colour.surface,
    border: `1px solid ${colour.borderSubtle}`,
    borderRadius: radius.base,
    padding: spacing.md,
  }

  return (
    <Flex vertical gap={spacing.md}>
      {groups.map((group) => {
        const single = group.name !== undefined && group.properties.length === 1 ? group.properties[0] : undefined
        const singleSchema = single === undefined ? undefined : resolveSchema(schema.properties![single], schema)
        const itemSchema = singleSchema?.type === 'array' ? singleSchema.items : undefined
        const items = single === undefined ? undefined : (value[single] as unknown)
        const fields = (
          <Flex vertical gap="small">
            {group.properties.map((name) =>
              renderProperty({
                name,
                child: schema.properties![name],
                node: schema,
                root: schema,
                rootValue: value,
                pointer: '',
                path: '',
                value,
                onChange,
                readOnlyNames,
                t,
                kind,
                named: name === single,
              }),
            )}
          </Flex>
        )
        if (group.name === undefined) {
          return (
            <div key="rest" data-testid="studio-section-rest" style={panel}>
              {fields}
            </div>
          )
        }
        const id = `${headingId}-${group.name}`
        const prefix = `shell.studio.section.${String(kind)}.${group.name}`
        return (
          <section key={group.name} aria-labelledby={id} data-testid={`studio-section-${group.name}`} style={panel}>
            <Flex justify="space-between" align="flex-start" gap="small" style={{ marginBottom: spacing.sm }}>
              <div>
                <Typography.Title id={id} level={5} style={{ margin: 0 }}>
                  {t(prefix)}
                </Typography.Title>
                <Typography.Text type="secondary">{t(`${prefix}.line`)}</Typography.Text>
              </div>
              {single !== undefined && itemSchema !== undefined && (
                <AxisButton
                  role="secondary"
                  onClick={() =>
                    onChange(`/${escapeSegment(single)}`, [
                      ...(Array.isArray(items) ? items : []),
                      defaultFor(itemSchema, schema),
                    ])
                  }
                >
                  {t('shell.studio.addItem')}
                </AxisButton>
              )}
            </Flex>
            {fields}
          </section>
        )
      })}
    </Flex>
  )
}

interface PropertyContext extends NodeProps {
  name: string
  child: JsonSchema
  readOnlyNames?: Set<string>
  t: (key: string) => string
  kind: unknown
  /** Whether the heading of a section names this property, see {@link SchemaNode}. */
  named?: boolean
}

/** The node of one property of an object, with its label and help from the kind's field texts. */
function renderProperty({
  name,
  child,
  node,
  root,
  rootValue,
  pointer,
  path,
  value,
  onChange,
  readOnlyNames,
  t,
  kind,
  named,
}: PropertyContext): ReactNode {
  const childPath = path === '' ? name : `${path}.${name}`
  const { label, help } = fieldTexts(t, kind, childPath, name)
  return (
    <SchemaNode
      key={name}
      node={child}
      root={root}
      rootValue={rootValue}
      pointer={`${pointer}/${escapeSegment(name)}`}
      path={childPath}
      name={label}
      help={help}
      value={(value as Record<string, unknown> | undefined)?.[name]}
      required={new Set(node.required).has(name)}
      readOnly={readOnlyNames?.has(name) ?? false}
      named={named}
      onChange={onChange}
    />
  )
}

function Properties({ node, root, rootValue, pointer, path, value, onChange }: NodeProps) {
  const t = useText()
  const kind = root.properties?.kind?.const
  return (
    <Flex vertical gap="small">
      {Object.entries(node.properties ?? {}).map(([name, child]) =>
        renderProperty({ name, child, node, root, rootValue, pointer, path, value, onChange, t, kind }),
      )}
    </Flex>
  )
}

/** The label and help of a property, from its field text, or its own name and no help when the kind has no text for it. */
function fieldTexts(
  t: (key: string) => string,
  kind: unknown,
  path: string,
  name: string,
): { label: string; help?: string } {
  if (typeof kind !== 'string') {
    return { label: name }
  }
  const key = `shell.studio.field.${kind}.${path}`
  if (t(key) === key) {
    return { label: name }
  }
  const help = t(`${key}.help`)
  return { label: t(key), help: help === `${key}.help` ? undefined : help }
}

function SchemaNode({
  node,
  root,
  rootValue,
  pointer,
  path,
  name,
  help,
  value,
  required,
  readOnly = false,
  item = false,
  named = false,
  onChange,
}: NodeProps & {
  name: string
  help?: string
  required: boolean
  readOnly?: boolean
  item?: boolean
  /** Whether the heading of a section names this field, so it shows no label, help or group of its own. */
  named?: boolean
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  const check = useContext(ExpressionCheckContext)
  const label = useContext(LabelTextsContext)
  const layer = useContext(SchemaLayerContext)
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
  const labelStyle = { fontWeight: 500, color: colour.textMuted }
  const helpNode =
    help === undefined ? undefined : (
      <div style={{ fontSize: 12, lineHeight: '20px', color: colour.textMuted, marginBottom: spacing.xs }}>{help}</div>
    )
  const groupStyle = { borderInlineStart: `1px solid ${colour.borderSubtle}`, paddingInlineStart: spacing.md }

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
      <Suspense fallback={<Input aria-label={name} value={text} readOnly aria-busy="true" />}>
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
        path={path}
        name={name}
        value={value}
        onChange={onChange}
      />
    )
  } else if (isLabel) {
    input = (
      <fieldset style={{ margin: 0, padding: 0, border: 0, minWidth: 0 }}>
        <legend style={labelStyle}>{name}</legend>
        {helpNode}
        <div data-testid={`studio-group-${pointer}`} style={groupStyle}>
          <LabelTexts pointer={pointer} value={value} onChange={onChange} />
        </div>
      </fieldset>
    )
  } else if (isObject && named) {
    input = (
      <Properties
        node={schema}
        root={root}
        rootValue={rootValue}
        pointer={pointer}
        path={path}
        value={value}
        onChange={onChange}
      />
    )
  } else if (isObject) {
    input = (
      <fieldset style={{ margin: 0, padding: 0, border: 0, minWidth: 0 }}>
        <legend style={labelStyle}>{name}</legend>
        {helpNode}
        <div data-testid={`studio-group-${pointer}`} style={groupStyle}>
          <Properties
            node={schema}
            root={root}
            rootValue={rootValue}
            pointer={pointer}
            path={path}
            value={value}
            onChange={onChange}
          />
        </div>
      </fieldset>
    )
  } else if (isArray) {
    const items = Array.isArray(value) ? value : []
    const itemSchema = schema.items!
    const keyProperty = layer === undefined ? undefined : keyPropertyOf(layer.keyedLists, pointer)
    const keyOfItem = (element: unknown): string | undefined => {
      const key =
        typeof element === 'object' && element !== null ? (element as Record<string, unknown>)[keyProperty!] : element
      return typeof key === 'string' ? key : undefined
    }
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
              {layer !== undefined && keyProperty !== undefined && keyOfItem(item) !== undefined && (
                <LayerMarkTag layer={layer.designer} list={pointer} index={index} name={keyOfItem(item)!} />
              )}
              <SchemaNode
                node={itemSchema}
                root={root}
                rootValue={rootValue}
                pointer={`${pointer}/${index}`}
                path={path}
                name={`${name} ${index + 1}`}
                value={item}
                required
                item
                onChange={onChange}
              />
            </div>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => onChange(`${pointer}/${index}`, undefined)}>{t('shell.studio.removeItem')}</Button>
          </Flex>
        ))}
        {keyProperty !== undefined && <RemovedRows layer={layer!.designer} list={pointer} />}
        {!named && (
          <div>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => onChange(pointer, [...items, defaultFor(itemSchema, root)])}>
              {t('shell.studio.addItem')}
            </Button>
          </div>
        )}
      </Flex>
    )
  }

  const showsLabel = !isObject && !named
  return (
    <div data-testid={`studio-field-${pointer}`}>
      {showsLabel && <div style={labelStyle}>{name}</div>}
      {showsLabel && helpNode}
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
  path,
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
  path: string
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
              path={path}
              name={key}
              value={entry}
              required
              item
              onChange={onChange}
            />
          </div>
          {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
          <Button onClick={() => onChange(`${pointer}/${escapeSegment(key)}`, undefined)}>
            {t('shell.studio.removeItem')}
          </Button>
        </Flex>
      ))}
      <div>
        {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
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
