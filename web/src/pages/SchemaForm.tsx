import { Button, Checkbox, Flex, Input, InputNumber, Select, Typography } from 'antd'
import { Link } from 'react-router'
import { createContext, lazy, Suspense, useContext, useId, useState, type CSSProperties, type ReactNode } from 'react'
import type { StudioCatalog, StudioFolderRelaxedPoint, StudioNames } from '../platform/studio'
import { setAt } from '../platform/jsonPatch'
import { keyPropertyOf, markOf, type KeyedLists } from '../platform/layerMarks'
import {
  catalogControlOf,
  isTableOnlyField,
  parseVersionRange,
  slotTitle,
  StudioCatalogContext,
  StudioRelaxedContext,
  versionRangeWords,
  type CatalogControl,
} from '../platform/studioCatalog'
import { getAt, parameterType, referenceOptions, StudioNamesContext, textsOf } from '../platform/studioNames'
import { resolveSchema, type JsonSchema, type ReferenceMark } from '../platform/studioSchemas'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { AxisButton } from '../platform/AxisButton'
import { PlatformContext } from './context'
import { relaxedPointLabel } from './relaxEntries'
import { CatalogChoice, ModuleSlots, TimeZoneSelect, VersionRangeInput } from './CatalogControls'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { ObjectListTable, type ObjectListColumn } from './ObjectListTable'
import { LayerMarkTag, RemovedRows, type DesignerLayer } from './LayerMarkViews'
import { ReferencePicker } from './ReferencePicker'
import { SchemaLayerContext } from './schemaLayerContext'
import { sectionsByKind } from './schemaFormSections'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

/** Whether the node is drawn inside an item's dialog, where a nested list is rows of inputs and never a table. */
const ItemDialogContext = createContext(false)

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
        const itemResolved = itemSchema === undefined ? undefined : resolveSchema(itemSchema, schema)
        // A list of objects draws its own Add… into the slot in the heading, see ObjectList.
        const tableInHeading = itemResolved !== undefined && isObjectList(itemResolved)
        const fieldsIn = (slot: HTMLElement | null | undefined) => (
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
                addIn: name === single && tableInHeading ? slot : undefined,
              }),
            )}
          </Flex>
        )
        if (group.name === undefined) {
          return (
            <div key="rest" data-testid="studio-section-rest" style={panel}>
              {fieldsIn(undefined)}
            </div>
          )
        }
        const id = `${headingId}-${group.name}`
        const prefix = `shell.studio.section.${String(kind)}.${group.name}`
        return (
          <HeadedSection
            key={group.name}
            id={id}
            name={group.name}
            title={t(prefix)}
            line={t(`${prefix}.line`)}
            style={panel}
            tableInHeading={tableInHeading}
            add={
              single !== undefined && itemSchema !== undefined && !tableInHeading ? (
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
              ) : null
            }
            fieldsIn={fieldsIn}
          />
        )
      })}
    </Flex>
  )
}

/**
 * A section with its heading. A section that holds one list of objects has a slot on the right of the heading, which
 * the list draws its Add… into, and a section that holds one list of plain values has the Add of `add` there.
 */
function HeadedSection({
  id,
  name,
  title,
  line,
  style,
  tableInHeading,
  add,
  fieldsIn,
}: {
  id: string
  name: string
  title: string
  line: string
  style: CSSProperties
  tableInHeading: boolean
  add: ReactNode
  fieldsIn: (slot: HTMLElement | null | undefined) => ReactNode
}) {
  const { spacing } = useDesignTokens()
  const [slot, setSlot] = useState<HTMLElement | null>(null)
  return (
    <section aria-labelledby={id} data-testid={`studio-section-${name}`} style={style}>
      <Flex justify="space-between" align="flex-start" gap="small" style={{ marginBottom: spacing.sm }}>
        <div>
          <Typography.Title id={id} level={5} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          <Typography.Text type="secondary">{line}</Typography.Text>
        </div>
        {tableInHeading ? <div ref={setSlot} /> : add}
      </Flex>
      {fieldsIn(tableInHeading ? slot : undefined)}
    </section>
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
  /** The slot in the section's heading that a list of objects draws its Add… into, see {@link SchemaNode}. */
  addIn?: Element | null
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
  addIn,
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
      addIn={addIn}
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
  addIn,
  onChange,
}: NodeProps & {
  name: string
  help?: string
  required: boolean
  readOnly?: boolean
  item?: boolean
  /** Whether the heading of a section names this field, so it shows no label, help or group of its own. */
  named?: boolean
  /** The element that a list of objects draws its Add… into, in place of the toolbar row of its table. */
  addIn?: Element | null
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const inDialog = useContext(ItemDialogContext)
  const names = useContext(StudioNamesContext)
  const catalog = useContext(StudioCatalogContext)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const check = useContext(ExpressionCheckContext)
  const label = useContext(LabelTextsContext)
  const layer = useContext(SchemaLayerContext)
  const schema = textAlternative(resolveSchema(node, root), root, value)
  const control = readOnly
    ? undefined
    : usableControl(catalogControlOf(root.properties?.kind?.const, path), catalog, names, rootValue, pointer, value)
  const reference = node['x-axis-reference'] ?? schema['x-axis-reference']
  const isObject = schema.type === 'object' && schema.properties !== undefined
  const isArray = schema.type === 'array' && schema.items !== undefined
  // A label is an object whose only property is `textKey`. It needs the resource to show its texts.
  const isLabel = isObject && label !== null && isLabelSchema(schema)
  const keyMark = schema.propertyNames?.['x-axis-reference']
  const isMarkedMap =
    schema.type === 'object' && schema.properties === undefined && keyMark !== undefined && names !== undefined
  const unsupported = control === undefined && !isMarkedMap && !isSupported(schema)
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
  } else if (control !== undefined && catalog !== undefined) {
    input = catalogInput({ control, catalog, names, name, locale, rootValue, pointer, value, onChange })
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
  } else if (isArray && !inDialog && isObjectList(resolveSchema(schema.items!, root))) {
    input = (
      <ObjectList
        itemSchema={resolveSchema(schema.items!, root)}
        root={root}
        rootValue={rootValue}
        pointer={pointer}
        path={path}
        value={value}
        addIn={addIn}
        onChange={onChange}
      />
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
              {t(inDialog ? 'shell.studio.list.addRow' : 'shell.studio.addItem')}
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
 * The catalog control that a field can draw now, see docs/reference/studio-resource-form.md#controls-that-know-what-they-hold,
 * or `undefined` when the field keeps its input: the catalog or the names have not loaded, the item's module or package
 * is not installed, or a version range is in no valid form.
 */
function usableControl(
  control: CatalogControl | undefined,
  catalog: StudioCatalog | undefined,
  names: StudioNames | undefined,
  rootValue: unknown,
  pointer: string,
  value: unknown,
): CatalogControl | undefined {
  if (control === undefined || catalog === undefined) {
    return undefined
  }
  switch (control) {
    case 'moduleVersion':
    case 'packageVersion':
      return typeof value === 'string' &&
        parseVersionRange(value) !== undefined &&
        installedOf(catalog, control === 'moduleVersion' ? 'module' : 'package', rootValue, pointer) !== undefined
        ? control
        : undefined
    case 'roles':
    case 'connectors':
      return names !== undefined && installedModule(catalog, rootValue, pointer) !== undefined ? control : undefined
    default:
      return control
  }
}

/** The installed module or package that the item holding the property at `pointer` names, if it is installed. */
function installedOf(catalog: StudioCatalog, kind: 'module' | 'package', rootValue: unknown, pointer: string) {
  const id = getAt(rootValue, `${itemPointerOf(pointer)}/id`)
  return (kind === 'module' ? catalog.modules : catalog.packages).find((entry) => entry.id === id)
}

/** The installed module that the item holding the property at `pointer` names, if it is installed. */
function installedModule(catalog: StudioCatalog, rootValue: unknown, pointer: string) {
  const id = getAt(rootValue, `${itemPointerOf(pointer)}/id`)
  return catalog.modules.find((entry) => entry.id === id)
}

function itemPointerOf(pointer: string): string {
  return pointer.slice(0, pointer.lastIndexOf('/'))
}

/** The control of a field that chooses from the catalog. `usableControl` has checked that the catalog has what it needs. */
function catalogInput({
  control,
  catalog,
  names,
  name,
  locale,
  rootValue,
  pointer,
  value,
  onChange,
}: {
  control: CatalogControl
  catalog: StudioCatalog
  names: StudioNames | undefined
  name: string
  locale: string
  rootValue: unknown
  pointer: string
  value: unknown
  onChange: ChangeHandler
}): ReactNode {
  const text = typeof value === 'string' ? value : undefined
  const itemPointer = itemPointerOf(pointer)
  switch (control) {
    case 'timeZone':
      return (
        <TimeZoneSelect
          name={name}
          zones={catalog.timeZones}
          value={text}
          onChange={(next) => onChange(pointer, next === undefined || next === '' ? undefined : next)}
        />
      )
    case 'module':
    case 'package': {
      const item = getAt(rootValue, itemPointer)
      return (
        <CatalogChoice
          name={name}
          installed={control === 'module' ? catalog.modules : catalog.packages}
          value={text}
          item={isRecord(item) ? item : {}}
          itemPointer={itemPointer}
          onChange={onChange}
        />
      )
    }
    case 'moduleVersion':
    case 'packageVersion':
      return (
        <VersionRangeInput
          range={parseVersionRange(text ?? '')!}
          versions={
            installedOf(catalog, control === 'moduleVersion' ? 'module' : 'package', rootValue, pointer)!.versions
          }
          onChange={(next) => onChange(pointer, next)}
        />
      )
    case 'roles':
    case 'connectors': {
      const installed = installedModule(catalog, rootValue, pointer)!
      const slots = control === 'roles' ? installed.roles : installed.connectors
      return (
        <ModuleSlots
          slots={slots}
          noSlots={control === 'roles' && installed.roles.length === 0 && installed.connectors.length === 0}
          names={referenceOptions(names!, control === 'roles' ? 'role' : 'connector', rootValue, pointer)}
          value={value}
          pointer={pointer}
          locale={locale}
          onChange={onChange}
        />
      )
    }
  }
}

/** Whether the item schema is an object with properties that is not a label, which a list draws as a table. */
function isObjectList(item: JsonSchema): boolean {
  return item.type === 'object' && item.properties !== undefined && !isLabelSchema(item)
}

/** The words a cell is written in: Yes and No, and a label's text. */
interface CellWords {
  yes: string
  no: string
  label: (key: string) => string
}

/**
 * The text of a value in words: a string or number as it is, a boolean as Yes or No, a label by its text, a list by its
 * items and an object or a map by its non-empty values, each joined by ", ". It is empty for a value that is not set.
 */
function wordsOf(schema: JsonSchema | undefined, root: JsonSchema, value: unknown, words: CellWords): string {
  const resolved = schema === undefined ? undefined : resolveSchema(schema, root)
  if (typeof value === 'string') {
    return value
  }
  if (typeof value === 'number') {
    return String(value)
  }
  if (typeof value === 'boolean') {
    return value ? words.yes : words.no
  }
  if (Array.isArray(value)) {
    return joinWords(value.map((item) => wordsOf(resolved?.items, root, item, words)))
  }
  if (!isRecord(value)) {
    return ''
  }
  if (resolved !== undefined && isLabelSchema(resolved)) {
    return typeof value.textKey === 'string' && value.textKey !== '' ? words.label(value.textKey) : ''
  }
  const properties = resolved?.properties
  return joinWords(
    properties === undefined
      ? entryLines(resolved, root, value, words)
      : Object.entries(properties).map(([name, child]) => wordsOf(child, root, value[name], words)),
  )
}

function joinWords(parts: string[]): string {
  return parts.filter((part) => part !== '').join(', ')
}

/** The entries of a map as `key: value`, without the entries that have no value. */
function entryLines(
  schema: JsonSchema | undefined,
  root: JsonSchema,
  map: Record<string, unknown>,
  words: CellWords,
): string[] {
  const valueSchema = typeof schema?.additionalProperties === 'object' ? schema.additionalProperties : undefined
  return Object.entries(map).flatMap(([key, entry]) => {
    const text = wordsOf(valueSchema, root, entry, words)
    return text === '' ? [] : [`${key}: ${text}`]
  })
}

/** The cell of a property in words: a nested list or a map is one line for each item or entry, else a text. */
function cellWords(schema: JsonSchema, root: JsonSchema, value: unknown, words: CellWords): ReactNode {
  const resolved = resolveSchema(schema, root)
  if (Array.isArray(value) || (isRecord(value) && resolved.properties === undefined)) {
    const lines = (
      Array.isArray(value)
        ? value.map((item) => wordsOf(resolved.items, root, item, words))
        : entryLines(resolved, root, value as Record<string, unknown>, words)
    ).filter((line) => line !== '')
    return lines.length === 0 ? null : (
      <Flex vertical>
        {lines.map((line, index) => (
          <span key={index}>{line}</span>
        ))}
      </Flex>
    )
  }
  const text = wordsOf(resolved, root, value, words)
  return text === '' ? null : text
}

/**
 * A module's relaxed points in the Modules table, each a link to its contract's page, then its reason, see
 * docs/reference/studio-resource-form.md#relaxed-points-in-settings. A point whose contract the server cannot find is
 * plain text.
 */
function RelaxedPointLines({ application, points }: { application: string; points: StudioFolderRelaxedPoint[] }) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { spacing } = useDesignTokens()
  return (
    <Flex vertical gap={spacing.sm}>
      {points.map((point) => {
        const label = relaxedPointLabel(point, locale, t)
        return (
          <Flex key={point.point} vertical data-testid="studio-relaxed-point">
            {point.contractId === null ? (
              <span>{label}</span>
            ) : (
              <Link to={`/studio?${new URLSearchParams({ app: application, resource: point.contractId })}`}>
                {label}
              </Link>
            )}
            <span>{point.reason}</span>
          </Flex>
        )
      })}
    </Flex>
  )
}

/** The item's name in Add… and the dialog's title, from `shell.studio.item.<kind>.<path>`, or a plain "item". */
function itemNameOf(t: (key: string) => string, kind: unknown, path: string): string {
  const key = `shell.studio.item.${String(kind)}.${path}`
  return typeof kind === 'string' && t(key) !== key ? t(key) : t('shell.studio.list.item')
}

/**
 * A list of objects as a table with an item dialog, see docs/reference/studio-resource-form.md#lists-of-objects. The
 * dialog draws the item's fields at their real pointers on a copy. The label texts typed in it are held here and
 * written with the item on Done, so Cancel leaves them as they were. A module layer's tag shows in the first cell, and
 * the rows the layer removed show below the table.
 */
function ObjectList({
  itemSchema,
  root,
  rootValue,
  pointer,
  path,
  value,
  addIn,
  onChange,
}: {
  itemSchema: JsonSchema
  root: JsonSchema
  rootValue: unknown
  pointer: string
  path: string
  value: unknown
  addIn?: Element | null
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const labels = useContext(LabelTextsContext)
  const layer = useContext(SchemaLayerContext)
  const check = useContext(ExpressionCheckContext)
  const catalog = useContext(StudioCatalogContext)
  const relaxed = useContext(StudioRelaxedContext)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour } = useDesignTokens()
  const [held, setHeld] = useState<Record<string, { pointer: string; texts: Record<string, string> }>>({})
  const kind = root.properties?.kind?.const
  const items = (Array.isArray(value) ? value : []).filter(isRecord)
  const properties = Object.entries(itemSchema.properties ?? {})
  const keyProperty = layer === undefined ? undefined : keyPropertyOf(layer.keyedLists, pointer)

  const words: CellWords = {
    yes: t('shell.table.yes'),
    no: t('shell.table.no'),
    label: (key) => {
      if (names === undefined) {
        return ''
      }
      const texts = labels?.texts[key] ?? textsOf(names, key)
      return texts[names.locales[0]] || Object.values(texts).find((text) => text !== '') || ''
    },
  }
  const columns: ObjectListColumn[] = properties.map(([name, child]) => {
    const resolved = resolveSchema(child, root)
    const control = catalogControlOf(kind, `${path}.${name}`)
    return {
      key: name,
      title: fieldTexts(t, kind, `${path}.${name}`, name).label,
      kind:
        resolved.type === 'boolean' ||
        resolved.type === 'integer' ||
        resolved.type === 'number' ||
        resolved.enum !== undefined ||
        control?.endsWith('Version')
          ? 'fixed'
          : 'freeText',
      render: (item) => {
        const cell = item[name]
        if (isTableOnlyField(kind, `${path}.${name}`)) {
          const points = relaxed?.points.filter((point) => point.module === item.id) ?? []
          return points.length === 0 ? null : <RelaxedPointLines application={relaxed!.application} points={points} />
        }
        if (typeof cell === 'string' && control?.endsWith('Version')) {
          return versionRangeWords(cell, t)
        }
        if (typeof cell === 'string' && (control === 'module' || control === 'package')) {
          return (
            catalog?.[control === 'module' ? 'modules' : 'packages'].find((entry) => entry.id === cell)?.label ?? cell
          )
        }
        const slots = control === 'roles' ? catalog?.modules.find((entry) => entry.id === item.id)?.roles : undefined
        if (slots !== undefined && slots.length > 0) {
          const roles = isRecord(cell) ? cell : {}
          return (
            <Flex vertical>
              {slots.map((slot) => {
                const role = roles[slot.name]
                return (
                  <span key={slot.name}>
                    {slotTitle(slot, locale)}:{' '}
                    {typeof role === 'string' && role !== '' ? (
                      role
                    ) : (
                      <span style={{ color: colour.warningText }}>{t('shell.studio.module.notMapped')}</span>
                    )}
                  </span>
                )
              })}
            </Flex>
          )
        }
        return cellWords(child, root, cell, words)
      },
    }
  })
  // The dialog leaves out the fields that only the table shows. Its copy still carries them, so Done writes them back.
  const dialogSchema: JsonSchema = {
    ...itemSchema,
    properties: Object.fromEntries(properties.filter(([name]) => !isTableOnlyField(kind, `${path}.${name}`))),
  }
  const large = Object.values(dialogSchema.properties ?? {}).some((child) => {
    const resolved = resolveSchema(child, root)
    return resolved.type === 'array' || (resolved.type === 'object' && resolved.properties === undefined)
  })

  return (
    <>
      <ObjectListTable
        pointer={pointer}
        items={items}
        columns={columns}
        itemName={itemNameOf(t, kind, path)}
        required={itemSchema.required ?? []}
        large={large}
        newItem={() => defaultFor(itemSchema, root) as Record<string, unknown>}
        addIn={addIn}
        onChange={onChange}
        onDone={() => {
          for (const [key, text] of Object.entries(held)) {
            labels?.onTextsChange(text.pointer, key, text.texts)
          }
          setHeld({})
        }}
        onCancel={() => setHeld({})}
        firstCellMark={(index) => {
          const key = keyProperty === undefined ? undefined : items[index][keyProperty]
          return layer !== undefined && typeof key === 'string' && markOf(layer.designer.elements, pointer, key) ? (
            <LayerMarkTag layer={layer.designer} list={pointer} index={index} name={key} />
          ) : null
        }}
        fields={(copy, change, index) => {
          const itemPointer = `${pointer}/${index}`
          const view = setAt(rootValue, pointer, index < items.length ? items.with(index, copy) : [...items, copy])
          const changeItem: ChangeHandler = (changed, next) => {
            if (changed === itemPointer || changed.startsWith(`${itemPointer}/`)) {
              change(changed.slice(itemPointer.length), next)
            }
          }
          const fields = (
            <ExpressionCheckContext.Provider value={check && { ...check, draft: view as Record<string, unknown> }}>
              <ItemDialogContext.Provider value>
                <Properties
                  node={dialogSchema}
                  root={root}
                  rootValue={view}
                  pointer={itemPointer}
                  path={path}
                  value={copy}
                  onChange={changeItem}
                />
              </ItemDialogContext.Provider>
            </ExpressionCheckContext.Provider>
          )
          return labels === null ? (
            fields
          ) : (
            <LabelTextsContext.Provider
              value={{
                ...labels,
                texts: {
                  ...labels.texts,
                  ...Object.fromEntries(Object.entries(held).map(([key, h]) => [key, h.texts])),
                },
                onTextsChange: (at, key, texts) =>
                  setHeld((current) => ({ ...current, [key]: { pointer: at, texts } })),
              }}
            >
              {fields}
            </LabelTextsContext.Provider>
          )
        }}
      />
      {layer !== undefined && keyProperty !== undefined && <RemovedRows layer={layer.designer} list={pointer} />}
    </>
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
