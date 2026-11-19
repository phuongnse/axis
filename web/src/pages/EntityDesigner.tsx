import { Button, ConfigProvider, Drawer, Flex, Input, InputNumber, Select, Tag, Typography, DatePicker } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import dayjs from 'dayjs'
import { memo, useCallback, useContext, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { AxisCheckboxField } from '../platform/AxisCheckboxField'
import { AxisChoiceField } from '../platform/AxisChoiceField'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import { FilledText } from '../platform/FilledText'
import { booleanText } from '../platform/formatValue'
import {
  changeFieldType,
  editableSettings,
  fieldSettings,
  fieldTypes,
  type FieldSetting,
} from '../platform/entityFields'
import type { FieldOrigin, FieldWarning } from '../platform/entityMigrations'
import { escapePointerSegment } from '../platform/jsonPatch'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { labelText, referenceOptions, StudioNamesContext, type ReferenceOption } from '../platform/studioNames'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useTableView } from '../platform/useTableView'
import { LabelTexts } from './LabelTexts'
import { type DesignerLayer, LayerMarkTag, RemovedRows } from './LayerMarkViews'
import { PlatformContext } from './context'
import { Expression } from './ProcessFields'
import { uniqueStepName } from './processPalette'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'
import { setOwn } from '../platform/ownProperty'

type Content = Record<string, unknown>

/** The settings that the field drawer shows after the help, in the order it shows them. `required` and `unique` come before them. */
const drawerSettings: FieldSetting[] = [
  'maxLength',
  'minLength',
  'min',
  'max',
  'precision',
  'scale',
  'maxSize',
  'accept',
  'target',
  'currency',
  'values',
  'valueLabels',
  'expression',
  'default',
  'sequence',
  'creator',
  'searchable',
  'sensitive',
  'exactLookup',
]

/** Joins the field names into one text that tells whether the list changed. */
const nameSeparator = '\u0000'

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/** A placeholder for what the designer cannot show, which points to the JSON view. */
function JsonOnly() {
  const t = useText()
  return <span data-testid="studio-json-only">{t('shell.studio.editInJson')}</span>
}

/** The label and the help of an entity's input, from `shell.studio.field.entity.<path>`, see docs/reference/frontend.md#platform-field-help. */
function fieldTexts(t: (key: string) => string, path: string) {
  return { label: t(`shell.studio.field.entity.${path}`), help: t(`shell.studio.field.entity.${path}.help`) }
}

/** The title and the help of an entity's group of inputs, which has no one input to label. */
function groupTexts(t: (key: string) => string, path: string) {
  const { label, help } = fieldTexts(t, path)
  return { title: label, help }
}

/** The heading and the line of an entity's section, from `shell.studio.section.entity.<name>`, see docs/reference/entity-designer.md#sections. */
function sectionHeading(t: (key: string) => string, name: 'general' | 'fields' | 'validations' | 'indexes') {
  const prefix = `shell.studio.section.entity.${name}`
  return { title: t(prefix), line: t(`${prefix}.line`) }
}

/**
 * A picker over `options`, or over the application's names of `kind` when `options` is not given. It is a plain box
 * while the application's names have not loaded and `options` is not given.
 */
const Picker = memo(function Picker({
  pointer,
  label,
  help,
  kind,
  options,
  value,
  required,
  onPick,
}: {
  pointer: string
  label: string
  help: string
  kind?: string
  options?: (string | ReferenceOption)[]
  value: unknown
  required: boolean
  onPick: (next: string | undefined) => void
}) {
  const names = useContext(StudioNamesContext)
  const choices =
    options ??
    (names === undefined || kind === undefined ? undefined : referenceOptions(names, kind, undefined, pointer))

  return (
    <div data-testid={`studio-field-${pointer}`}>
      <AxisField label={label} help={help} required={required}>
        {choices === undefined ? (
          <Input
            value={text(value)}
            onChange={(event) => onPick(event.target.value === '' && !required ? undefined : event.target.value)}
          />
        ) : (
          <ReferencePicker
            name={label}
            value={typeof value === 'string' ? value : undefined}
            required={required}
            options={choices}
            onChange={onPick}
          />
        )}
      </AxisField>
    </div>
  )
})

/**
 * The designer of an entity, in the sections General, Fields, Validations and Indexes, see
 * docs/reference/entity-designer.md#sections. General shows the entity's name, label, description and display field.
 * Fields is a table of name, label, type, Required, Unique and properties, and a field's name opens the field drawer with
 * the field's inputs and the settings its type takes, see docs/reference/entity-designer.md#field-drawer and
 * docs/reference/entity-designer.md#fields. Validations is rows of a name, an
 * expression, a message key and a field. A field can be
 * added, removed and moved, and a type change drops the settings the new type does not take. A computed field's
 * `expression` and each validation's expression use the expression editor, and so does a field's `default`, which has
 * no field in scope. A field's settings also include `min` and `max` in the type of the field, `minLength` and
 * `searchable`, and they hide `default`, `min`, `max` and `minLength` next to an `expression`, a `sequence` or
 * `creator`. The Indexes section, after Validations, adds, moves and removes the entity's `indexes`, each a name, a
 * list of the entity's fields and a Unique choice. The name of an index or a validation is shown as plain text, and an added one gets `index1` or
 * `validation1`, the first name its list does not use. The designer edits the draft through `onChange`, so a key it
 * does not show, such as `id`, `kind` or an unknown key on a field, stays as it is. Every input has the id
 * `studio-field-<pointer>`, and each row of the table has the id of its field, so a diagnostic opens at its input or
 * at the row of a setting inside a field, which the drawer, drawn outside the form, holds. Clearing an optional value removes its key. With `readOnly`,
 * as for an entity of a module, every input is off, the label is its plain text key, and there are no buttons that
 * add, remove or move a field or a validation. With `origins`, the loaded name of each row of the fields table or
 * `null` for a row added in this session, Add field, Remove and Move report the origins that follow through
 * `onOriginsChange`, and Remove also reports the origin of the removed row as touched. With `warnings`, the row
 * numbers of fields whose change no migration follows, a warning shows at the top of that field's drawer. With `layer`,
 * as for a module entity that has a customization layer, a field, an index or a validation that the layer added or
 * changed shows an Added or Changed tag, found by its name and not by its position. The layer's removed ones show as
 * muted Removed rows under their list. Each has Reset to module, which calls `onReset` with the list and the name, and is
 * off while `layer.disabled`.
 */
export function EntityDesigner({
  value,
  onChange,
  readOnly = false,
  origins,
  warnings,
  onOriginsChange,
  layer,
}: {
  value: Content
  onChange: ChangeHandler
  readOnly?: boolean
  origins?: FieldOrigin[]
  warnings?: Map<number, FieldWarning>
  onOriginsChange?: (next: FieldOrigin[], touched?: string) => void
  layer?: DesignerLayer
}) {
  const t = useText()
  const fields = Array.isArray(value.fields) ? value.fields : undefined
  // The names, the same list while no name changes, so the rows that pick among them stay as they are on a key elsewhere.
  const joinedNames = (fields ?? [])
    .flatMap((field) => (isContent(field) && typeof field.name === 'string' && field.name !== '' ? [field.name] : []))
    .join(nameSeparator)
  const fieldNames = useMemo(() => (joinedNames === '' ? [] : joinedNames.split(nameSeparator)), [joinedNames])
  const pickDisplayField = useCallback((next: string | undefined) => onChange('/displayField', next), [onChange])

  const designer = (
    <div data-testid="studio-entity-designer">
      <SectionParts>
        <SectionPart testName="general" heading={sectionHeading(t, 'general')}>
          <Flex vertical gap="middle">
            <div data-testid="studio-field-/name">
              <AxisField {...fieldTexts(t, 'name')} required>
                <Input value={text(value.name)} onChange={(event) => onChange('/name', event.target.value)} />
              </AxisField>
            </div>
            <div data-testid="studio-field-/label">
              <AxisFieldGroup {...groupTexts(t, 'label')}>
                <LabelTexts pointer="/label" value={value.label} onChange={onChange} />
              </AxisFieldGroup>
            </div>
            <div data-testid="studio-field-/description">
              <AxisFieldGroup {...groupTexts(t, 'description')}>
                <LabelTexts pointer="/description" value={value.description} onChange={onChange} />
              </AxisFieldGroup>
            </div>
            <Picker
              pointer="/displayField"
              {...fieldTexts(t, 'displayField')}
              options={fieldNames}
              value={value.displayField}
              required={false}
              onPick={pickDisplayField}
            />
          </Flex>
        </SectionPart>
        <Fields
          fields={fields}
          readOnly={readOnly}
          origins={origins}
          warnings={warnings}
          onOriginsChange={onOriginsChange}
          layer={layer}
          onChange={onChange}
        />
        <Validations
          validations={value.validations}
          fieldNames={fieldNames}
          readOnly={readOnly}
          layer={layer}
          onChange={onChange}
        />
        <Indexes indexes={value.indexes} fields={fields ?? []} readOnly={readOnly} layer={layer} onChange={onChange} />
      </SectionParts>
    </div>
  )

  return readOnly ? <ConfigProvider componentDisabled>{designer}</ConfigProvider> : designer
}

interface FieldRow {
  index: number
  field: unknown
}

/** The origin of each row, `null` for a row that has none. */
function rowOrigins(fields: unknown[], origins: FieldOrigin[] | undefined): FieldOrigin[] {
  return fields.map((_, index) => origins?.[index] ?? null)
}

/** The columns of the Fields table that have a filter, by the name their filter has in the address. Properties has none. */
const fieldTableFields = [
  { name: 'name', type: 'text' },
  { name: 'label', type: 'text' },
  { name: 'type', type: 'enum' },
  { name: 'required', type: 'enum' },
  { name: 'unique', type: 'enum' },
] as const

/** Whether a field's type takes a yes-or-no setting, with its value, or `null` where the type does not take it. */
function yesNoSetting(field: unknown, setting: 'required' | 'unique'): boolean | null {
  if (!isContent(field) || !fieldSettings(text(field.type)).includes(setting)) {
    return null
  }
  return field[setting] === true
}

/**
 * A column of a yes-or-no setting, Yes or No, with the dash where the field's type does not take it. It filters as a
 * choice of Yes and No, and the dash is empty for the filter.
 */
function yesNoColumn(setting: 'required' | 'unique', title: string, t: (key: string) => string): AxisColumn<FieldRow> {
  return {
    key: setting,
    kind: 'fixed',
    title,
    filter: {
      kind: 'enum',
      value: ({ field }) => {
        const value = yesNoSetting(field, setting)
        return value === null ? null : value ? 'yes' : 'no'
      },
      options: [
        { value: 'yes', label: booleanText(true, t) },
        { value: 'no', label: booleanText(false, t) },
      ],
    },
    render: (_, { field }) => {
      const value = yesNoSetting(field, setting)
      return value === null ? null : booleanText(value, t)
    },
  }
}

/** The sources of a field's value, in the order the Properties column takes the first one a field has. */
const valueSources: [string, (field: Content) => boolean][] = [
  ['computed', (field) => typeof field.expression === 'string'],
  ['sequence', (field) => typeof field.sequence === 'string'],
  ['creator', (field) => field.creator === true],
  ['matched', (field) => isContent(field.match)],
  ['user', (field) => field.userId === true],
  ['default', (field) => typeof field.default === 'string'],
]

/** The property names a field shows as tags: at most one source of its value, then Searchable and Sensitive. */
function fieldProperties(field: unknown): string[] {
  if (!isContent(field)) {
    return []
  }
  const source = valueSources.find(([, has]) => has(field))
  return [...(source ? [source[0]] : []), ...['searchable', 'sensitive'].filter((name) => field[name] === true)]
}

/** The Fields table keeps no data source parameter in the address, only its view. */
const noParameters: string[] = []

/**
 * The fields of the entity as a table, in the Fields section, with Add field on the right of its heading. A row reads the
 * field's name as a link with its layer mark, its label, its type in words, Required and Unique as Yes or No, and its
 * properties as tags, see docs/reference/entity-designer.md#fields. The name opens the field drawer, which holds the
 * field's inputs, see docs/reference/entity-designer.md#field-drawer. Every cell is a memo component of stable
 * props, so a key in one row draws that row's cells and no other. Only the row has an id in the table, so a diagnostic
 * inside a field opens at its row. The table has a Filter on Name, Label, Type, Required and Unique, kept in the address as
 * `fields.filter`. A filter only hides rows: every row shows on one page, an edit to a hidden row stays in the draft, and
 * a field whose origin is `null`, one added since the last save, stays shown. Add field opens the new field's drawer.
 * Move up and Move down keep the open drawer on its field at the field's new index, and Remove closes the drawer only
 * when it removes that field, see docs/reference/entity-designer.md#field-drawer. A read-only designer has no "…" menu,
 * see docs/reference/entity-designer.md#fields.
 */
const Fields = memo(function Fields({
  fields,
  readOnly,
  origins,
  warnings,
  onOriginsChange,
  layer,
  onChange,
}: {
  fields: unknown[] | undefined
  readOnly: boolean
  origins: FieldOrigin[] | undefined
  warnings: Map<number, FieldWarning> | undefined
  onOriginsChange: ((next: FieldOrigin[], touched?: string) => void) | undefined
  layer: DesignerLayer | undefined
  onChange: ChangeHandler
}) {
  const t = useText()
  // The row of the field whose drawer is open or leaving. The drawer keeps its field while it closes, and shows as
  // closed when its row is gone, as after an Undo.
  const [drawer, setDrawer] = useState<{ index: number; open: boolean } | null>(null)
  const shown = drawer !== null && fields !== undefined && drawer.index < fields.length ? drawer.index : null
  const openDrawer = useCallback((index: number) => setDrawer({ index, open: true }), [])
  const closeDrawer = useCallback(() => setDrawer((current) => current && { ...current, open: false }), [])
  const { view, setView } = useTableView(fieldTableFields, recordPageSize, noParameters, false, true, false, 'fields.')
  // What the entries of a row's menu read when they are pressed, so they stay the same between draws.
  const latest = useRef({ fields, origins, onOriginsChange, onChange })
  useLayoutEffect(() => {
    latest.current = { fields, origins, onOriginsChange, onChange }
  })
  // The names a money field can take its currency from, by row: a text or enum field's name, else `null`.
  const currencySignature = (fields ?? [])
    .map((other) =>
      isContent(other) &&
      (other.type === 'enum' || other.type === 'text') &&
      typeof other.name === 'string' &&
      other.name !== ''
        ? other.name
        : '',
    )
    .join(nameSeparator)
  const currencyNames = useMemo(
    () => currencySignature.split(nameSeparator).map((name) => (name === '' ? null : name)),
    [currencySignature],
  )

  const onMove = useCallback((index: number, offset: number) => {
    const { fields, origins, onOriginsChange, onChange } = latest.current
    // The drawer's field moves with it, and a closed drawer keeps its index in step, so it never opens on another field.
    setDrawer(
      (current) =>
        current &&
        (current.index === index
          ? { ...current, index: index + offset }
          : current.index === index + offset
            ? { ...current, index }
            : current),
    )
    const next = [...fields!]
    ;[next[index], next[index + offset]] = [next[index + offset], next[index]]
    onChange('/fields', next)
    if (onOriginsChange) {
      const moved = rowOrigins(fields!, origins)
      ;[moved[index], moved[index + offset]] = [moved[index + offset], moved[index]]
      onOriginsChange(moved)
    }
  }, [])

  const onRemove = useCallback((index: number) => {
    const { fields, origins, onOriginsChange, onChange } = latest.current
    // Removing the drawer's own field closes the drawer. Removing a field before it moves the drawer up an index.
    setDrawer((current) =>
      current === null
        ? null
        : current.index === index
          ? { ...current, open: false }
          : current.index > index
            ? { ...current, index: current.index - 1 }
            : current,
    )
    onChange(
      '/fields',
      fields!.filter((_, position) => position !== index),
    )
    onOriginsChange?.(
      rowOrigins(fields!, origins).filter((_, position) => position !== index),
      origins?.[index] ?? undefined,
    )
  }, [])

  function addField() {
    onChange('/fields', [...fields!, { name: '', type: 'text' }])
    onOriginsChange?.([...rowOrigins(fields!, origins), null])
    openDrawer(fields!.length)
  }

  if (fields === undefined) {
    return (
      <SectionPart testName="fields" heading={sectionHeading(t, 'fields')}>
        <div data-testid="studio-field-/fields">
          <JsonOnly />
        </div>
      </SectionPart>
    )
  }

  const columns: AxisColumn<FieldRow>[] = [
    {
      key: 'name',
      kind: 'freeText',
      title: t('shell.studio.entity.fieldName'),
      filter: { kind: 'text', value: ({ field }) => text(isContent(field) ? field.name : undefined) || null },
      render: (_, { field, index }) => <NameCell index={index} field={field} layer={layer} onOpen={openDrawer} />,
    },
    {
      key: 'label',
      kind: 'freeText',
      title: t('shell.studio.entity.fieldLabel'),
      // The label's text key is what the file holds, so it is always there, unlike the text in the author's language.
      filter: {
        kind: 'text',
        value: ({ field }) => {
          const label = isContent(field) ? field.label : undefined
          return (isContent(label) ? text(label.textKey) : '') || null
        },
      },
      render: (_, { field }) => <LabelCell field={field} />,
    },
    {
      key: 'type',
      kind: 'freeText',
      title: t('shell.studio.entity.fieldType'),
      filter: {
        kind: 'enum',
        value: ({ field }) => text(isContent(field) ? field.type : undefined) || null,
        options: fieldTypes.map((option) => ({ value: option, label: option })),
      },
      render: (_, { field }) => <TypeCell field={field} />,
    },
    yesNoColumn('required', t('shell.studio.entity.required'), t),
    yesNoColumn('unique', t('shell.studio.entity.unique'), t),
    {
      key: 'properties',
      kind: 'fixed',
      title: t('shell.studio.entity.properties'),
      render: (_, { field }) => <PropertiesCell field={field} />,
    },
  ]

  return (
    <SectionPart
      testName="fields"
      heading={sectionHeading(t, 'fields')}
      action={
        readOnly ? undefined : (
          <AxisButton role="secondary" onClick={addField}>
            {t('shell.studio.entity.addField')}
          </AxisButton>
        )
      }
    >
      <div data-testid="studio-field-/fields">
        <Flex vertical gap="small">
          <AxisTable<FieldRow>
            dataSource={fields.map((field, index) => ({ field, index }))}
            view={{ query: view, onChange: setView, paging: false }}
            keep={(row) => origins !== undefined && (origins[row.index] ?? null) === null}
            rowKey={(row) => String(row.index)}
            emptyText={t('shell.table.empty')}
            rowAttributes={(row) =>
              ({ 'data-testid': `studio-field-/fields/${row.index}` }) as React.HTMLAttributes<HTMLElement>
            }
            rowActions={
              readOnly
                ? undefined
                : {
                    configured: 3,
                    actions: ({ index }) => [
                      {
                        key: 'moveUp',
                        labelKey: 'shell.studio.entity.moveUp',
                        disabled: index === 0,
                        onClick: () => onMove(index, -1),
                      },
                      {
                        key: 'moveDown',
                        labelKey: 'shell.studio.entity.moveDown',
                        disabled: index === fields.length - 1,
                        onClick: () => onMove(index, 1),
                      },
                      {
                        key: 'remove',
                        labelKey: 'shell.studio.entity.removeField',
                        destructive: true,
                        onClick: () => onRemove(index),
                      },
                    ],
                  }
            }
            columns={columns}
          />
          <RemovedRows layer={layer} list="/fields" />
        </Flex>
      </div>
      <FieldDrawer
        index={shown}
        open={drawer?.open === true}
        field={shown === null ? undefined : fields[shown]}
        currencyNames={
          shown !== null && isContent(fields[shown]) && editableSettings(fields[shown]).includes('currency')
            ? currencyNames
            : undefined
        }
        readOnly={readOnly}
        warning={shown === null ? undefined : warnings?.get(shown)}
        onChange={onChange}
        onClose={closeDrawer}
      />
    </SectionPart>
  )
})

/** The name of a field's row as a link that opens the field drawer, with the mark of a customization layer. */
const NameCell = memo(function NameCell({
  index,
  field,
  layer,
  onOpen,
}: {
  index: number
  field: unknown
  layer: DesignerLayer | undefined
  onOpen: (index: number) => void
}) {
  const t = useText()
  const name = text(isContent(field) ? field.name : undefined)
  return (
    <Flex gap="small" align="center" wrap>
      {/* A read-only designer turns its inputs off, but its name still opens the drawer. */}
      <AxisButton role="link" size="small" disabled={false} onClick={() => onOpen(index)}>
        {name || t('shell.studio.entity.unnamedField')}
      </AxisButton>
      {layer && <LayerMarkTag layer={layer} list="/fields" index={index} name={name} />}
    </Flex>
  )
})

/** The label of a field's row in the author's language, else its text key, and the dash when it has none. */
const LabelCell = memo(function LabelCell({ field }: { field: unknown }) {
  const names = useContext(StudioNamesContext)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const label = isContent(field) ? field.label : undefined
  const key = (isContent(label) ? text(label.textKey) : '') || undefined
  if (key === undefined) {
    return <EmptyDash />
  }
  return (names && labelText(names, key, locale)) || key
})

/**
 * The type of a field in words. For a reference, references or child collection, the target's label fills in its
 * place, or the target's name where the entity has no label. The target is matched by name, ignoring letter case, and a
 * name the application does not have reads as written. A type the designer does not know reads as written too, see
 * docs/reference/entity-designer.md#fields.
 */
const TypeCell = memo(function TypeCell({ field }: { field: unknown }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const type = text(isContent(field) ? field.type : undefined)
  if (type === '') {
    return <EmptyDash />
  }
  if (!(fieldTypes as readonly string[]).includes(type)) {
    return type
  }
  const target = text(isContent(field) ? field.target : undefined)
  if (target === '') {
    return <FilledText text={t(`shell.studio.entity.typeOf.${type}`)} parts={{ target: <EmptyDash /> }} />
  }
  const entity = names?.entities.find((other) => other.name.toLowerCase() === target.toLowerCase())
  const label = names && entity ? labelText(names, entity.labelKey, locale) : undefined
  return <FilledText text={t(`shell.studio.entity.typeOf.${type}`)} parts={{ target: label || target }} />
})

/** The properties of a field as tags, or the dash when it has none, see docs/reference/entity-designer.md#fields. */
const PropertiesCell = memo(function PropertiesCell({ field }: { field: unknown }) {
  const t = useText()
  const properties = fieldProperties(field)
  if (properties.length === 0) {
    return <EmptyDash />
  }
  return (
    <Flex gap="small" wrap>
      {properties.map((name) => (
        <Tag key={name}>{t(`shell.studio.entity.property.${name}`)}</Tag>
      ))}
    </Flex>
  )
})

/**
 * The drawer of a field, beside the table and above the save bar, with no mask so the table stays clickable, see
 * docs/reference/entity-designer.md#field-drawer. From the top it holds the migration warning, then the field's name,
 * label, type, help, Required, Unique and the settings its type takes, each an input with its help and its id.
 */
const FieldDrawer = memo(function FieldDrawer({
  index,
  open,
  field,
  currencyNames,
  readOnly,
  warning,
  onChange,
  onClose,
}: {
  index: number | null
  open: boolean
  field: unknown
  currencyNames: (string | null)[] | undefined
  readOnly: boolean
  warning: FieldWarning | undefined
  onChange: ChangeHandler
  onClose: () => void
}) {
  const t = useText()
  const { size } = useDesignTokens()
  const content = isContent(field) ? field : {}
  const name = text(content.name)
  const type = text(content.type)
  const taken = fieldSettings(type)
  const pointer = `/fields/${index}`

  function flag(setting: 'required' | 'unique') {
    return (
      taken.includes(setting) && (
        <div data-testid={`studio-field-${pointer}/${setting}`}>
          <AxisCheckboxField
            {...fieldTexts(t, `fields.${setting}`)}
            checked={content[setting] === true}
            onChange={(checked) => onChange(`${pointer}/${setting}`, checked)}
          />
        </div>
      )
    )
  }

  return (
    <Drawer
      open={open && index !== null}
      mask={false}
      size={size.dialog.medium}
      destroyOnHidden
      // The save bar shows as soon as there is an edit, so the drawer always leaves its height free.
      rootStyle={{ bottom: size.bar }}
      title={name || t('shell.studio.entity.unnamedField')}
      onClose={onClose}
    >
      {index !== null && (
        <Flex vertical gap="middle" data-testid="studio-field-drawer" key={index}>
          {warning && (
            <AxisAlert
              kind="warning"
              data-testid={`studio-migration-warning-${pointer}`}
              message={t(
                warning === 'enum'
                  ? 'shell.studio.entity.migration.enumWarning'
                  : 'shell.studio.entity.migration.uniqueWarning',
              )}
            />
          )}
          <div data-testid={`studio-field-${pointer}/name`}>
            <AxisField {...fieldTexts(t, 'fields.name')} required>
              <Input value={name} onChange={(event) => onChange(`${pointer}/name`, event.target.value)} />
            </AxisField>
          </div>
          <AxisField {...fieldTexts(t, 'fields.label')}>
            <FieldLabel
              name={fieldTexts(t, 'fields.label').label}
              pointer={`${pointer}/label`}
              value={content.label}
              onChange={onChange}
            />
          </AxisField>
          <div data-testid={`studio-field-${pointer}/type`}>
            <AxisField {...fieldTexts(t, 'fields.type')} required>
              <Select
                showSearch
                optionFilterProp="label"
                virtual={false}
                value={type === '' ? undefined : type}
                options={fieldTypes.map((option) => ({ value: option, label: option }))}
                onChange={(next: string) => onChange(pointer, changeFieldType(content, next))}
              />
            </AxisField>
          </div>
          <AxisField {...fieldTexts(t, 'fields.helpText')}>
            <FieldLabel
              name={fieldTexts(t, 'fields.helpText').label}
              pointer={`${pointer}/help`}
              value={content.help}
              onChange={onChange}
            />
          </AxisField>
          {flag('required')}
          {flag('unique')}
          <Settings
            index={index}
            field={content}
            currencyNames={currencyNames}
            readOnly={readOnly}
            onChange={onChange}
          />
        </Flex>
      )}
    </Drawer>
  )
})

/** The text key picker of a field's label or help, named `name`. Clearing it removes the text. */
const FieldLabel = memo(function FieldLabel({
  name,
  id,
  'aria-describedby': describedBy,
  pointer,
  value,
  onChange,
}: {
  name: string
  id?: string
  'aria-describedby'?: string
  pointer: string
  value: unknown
  onChange: ChangeHandler
}) {
  const names = useContext(StudioNamesContext)
  const key = isContent(value) && typeof value.textKey === 'string' ? value.textKey : undefined

  return (
    <div data-testid={`studio-field-${pointer}`}>
      {names === undefined ? (
        <Input
          aria-label={name}
          id={id}
          aria-describedby={describedBy}
          value={text(key)}
          onChange={(event) =>
            event.target.value === ''
              ? onChange(pointer, undefined)
              : onChange(`${pointer}/textKey`, event.target.value)
          }
        />
      ) : (
        <ReferencePicker
          name={name}
          id={id}
          aria-describedby={describedBy}
          value={key}
          required={false}
          options={referenceOptions(names, 'text', undefined, pointer)}
          onChange={(next) =>
            next === undefined ? onChange(pointer, undefined) : onChange(`${pointer}/textKey`, next)
          }
        />
      )}
    </div>
  )
})

/** The settings of one field that its type takes, each with its own input and id. */
function Settings({
  index,
  field,
  currencyNames,
  readOnly,
  onChange,
}: {
  index: number
  field: Content
  currencyNames: (string | null)[] | undefined
  readOnly: boolean
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/fields/${index}`
  const taken = editableSettings(field)
  const shown = drawerSettings.filter((setting) => taken.includes(setting))

  const settingTexts = (setting: FieldSetting) => fieldTexts(t, `fields.${setting}`)

  function number(setting: 'maxLength' | 'minLength' | 'precision' | 'scale' | 'maxSize') {
    return (
      <InputNumber
        precision={0}
        min={setting === 'maxSize' || setting === 'minLength' ? 1 : 0}
        value={typeof field[setting] === 'number' ? field[setting] : null}
        onChange={(next) => onChange(`${pointer}/${setting}`, typeof next === 'number' ? next : undefined)}
      />
    )
  }

  /** The yes-or-no setting `setting`: a switch for searchable, a checkbox for the others. */
  function flag(setting: 'creator' | 'sensitive' | 'exactLookup' | 'searchable') {
    return (
      <AxisCheckboxField
        {...settingTexts(setting)}
        kind={setting === 'searchable' ? 'switch' : 'checkbox'}
        checked={field[setting] === true}
        // Even a false exactLookup is refused on a field that is not sensitive, so unchecking removes it, as it does for searchable.
        onChange={(checked) =>
          onChange(
            `${pointer}/${setting}`,
            (setting === 'exactLookup' || setting === 'searchable') && !checked ? undefined : checked,
          )
        }
      />
    )
  }

  function input(setting: FieldSetting) {
    switch (setting) {
      case 'maxLength':
      case 'minLength':
      case 'precision':
      case 'scale':
      case 'maxSize':
        return number(setting)
      case 'min':
      case 'max':
        return (
          <Limit
            type={text(field.type)}
            value={field[setting]}
            onChange={(next) => onChange(`${pointer}/${setting}`, next)}
          />
        )
      case 'default':
        return (
          <Expression
            pointer={`${pointer}/default`}
            name={settingTexts(setting).label}
            value={text(field.default)}
            required={false}
            readOnly={readOnly}
            onChange={onChange}
          />
        )
      case 'values':
        return (
          <Select
            mode="tags"
            virtual={false}
            style={{ minWidth: 200 }}
            value={Array.isArray(field.values) ? field.values.filter((item) => typeof item === 'string') : []}
            onChange={(next: string[]) => onChange(`${pointer}/values`, next.length === 0 ? undefined : next)}
          />
        )
      case 'accept':
        return (
          <Select
            mode="tags"
            virtual={false}
            style={{ minWidth: 200 }}
            value={Array.isArray(field.accept) ? field.accept.filter((item) => typeof item === 'string') : []}
            onChange={(next: string[]) => onChange(`${pointer}/accept`, next.length === 0 ? undefined : next)}
          />
        )
      case 'valueLabels':
        return <ValueLabels pointer={`${pointer}/valueLabels`} field={field} onChange={onChange} />
      case 'expression':
        return (
          <Expression
            pointer={`${pointer}/expression`}
            name={settingTexts(setting).label}
            value={text(field.expression)}
            required={false}
            readOnly={readOnly}
            onChange={onChange}
          />
        )
      default:
        return null
    }
  }

  function labelled(setting: FieldSetting) {
    const control = input(setting)
    return control && <AxisField {...settingTexts(setting)}>{control}</AxisField>
  }

  return (
    <Flex vertical gap="small" style={{ minWidth: 240 }}>
      {shown.map((setting) =>
        setting === 'currency' ? (
          <CurrencySource
            key={setting}
            pointer={pointer}
            field={field}
            currencyFields={(currencyNames ?? []).flatMap((name, position) =>
              position !== index && name !== null ? [name] : [],
            )}
            onChange={onChange}
          />
        ) : setting === 'target' || setting === 'sequence' ? (
          <Picker
            key={setting}
            pointer={`${pointer}/${setting}`}
            {...settingTexts(setting)}
            kind={setting === 'target' ? 'entity' : 'sequence'}
            value={field[setting]}
            required={setting === 'target'}
            onPick={(next) => onChange(`${pointer}/${setting}`, next ?? (setting === 'target' ? '' : undefined))}
          />
        ) : (
          <div key={setting} data-testid={`studio-field-${pointer}/${setting}`}>
            {setting === 'creator' || setting === 'sensitive' || setting === 'exactLookup' || setting === 'searchable'
              ? flag(setting)
              : setting === 'valueLabels'
                ? input(setting)
                : labelled(setting)}
          </div>
        ),
      )}
    </Flex>
  )
}

/**
 * The `min` or `max` of a field, in the format of its type: a number for integer, decimal and money, a date written
 * `yyyy-MM-dd` and a date-time written as an RFC 3339 UTC string. Clearing it writes undefined.
 */
function Limit({
  type,
  value,
  id,
  'aria-describedby': describedBy,
  onChange,
}: {
  type: string
  value: unknown
  id?: string
  'aria-describedby'?: string
  onChange: (next: number | string | undefined) => void
}) {
  if (type === 'date') {
    return (
      <DatePicker
        id={id}
        aria-describedby={describedBy}
        value={typeof value === 'string' && dayjs(value).isValid() ? dayjs(value) : null}
        onChange={(date) => onChange(date ? date.format('YYYY-MM-DD') : undefined)}
      />
    )
  }
  if (type === 'date-time') {
    return (
      <DatePicker
        id={id}
        aria-describedby={describedBy}
        showTime
        format="YYYY-MM-DD HH:mm:ss"
        value={typeof value === 'string' && dayjs(value).isValid() ? dayjs(value) : null}
        // UTC to the second, as an RFC 3339 string.
        onChange={(date) =>
          onChange(date ? new Date(date.valueOf()).toISOString().replace(/\.\d{3}Z$/, 'Z') : undefined)
        }
      />
    )
  }
  return (
    <InputNumber
      id={id}
      aria-describedby={describedBy}
      precision={type === 'integer' ? 0 : undefined}
      value={typeof value === 'number' ? value : null}
      onChange={(next) => onChange(typeof next === 'number' ? next : undefined)}
    />
  )
}

/**
 * The currency of a money field: a fixed code or the name of a field that holds it. The field's keys say which, so
 * the field holds `currencyField` or `currency`, never both. A typed code is turned into upper case.
 */
function CurrencySource({
  pointer,
  field,
  currencyFields,
  onChange,
}: {
  pointer: string
  field: Content
  currencyFields: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const fromField = Object.hasOwn(field, 'currencyField')
  const source = fieldTexts(t, 'fields.currencySource')

  function switchTo(next: 'code' | 'field') {
    if ((next === 'field') === fromField) {
      return
    }
    const rebuilt: Content = {}
    for (const [key, value] of Object.entries(field)) {
      if (key !== 'currency' && key !== 'currencyField') {
        setOwn(rebuilt, key, value)
      }
    }
    if (next === 'field') {
      rebuilt.currencyField = ''
    }
    onChange(pointer, rebuilt)
  }

  return (
    <Flex vertical gap="small">
      <AxisChoiceField
        {...source}
        value={fromField ? 'field' : 'code'}
        onChange={(next) => switchTo(next === 'field' ? 'field' : 'code')}
        options={[
          { value: 'code', ...fieldTexts(t, 'fields.currencySource.code') },
          { value: 'field', ...fieldTexts(t, 'fields.currencySource.field') },
        ]}
      />
      {fromField ? (
        <Picker
          pointer={`${pointer}/currencyField`}
          {...fieldTexts(t, 'fields.currencyField')}
          options={currencyFields}
          value={field.currencyField}
          required
          onPick={(next) => onChange(`${pointer}/currencyField`, next ?? '')}
        />
      ) : (
        <div data-testid={`studio-field-${pointer}/currency`}>
          <AxisField {...fieldTexts(t, 'fields.currency')}>
            <Input
              value={text(field.currency)}
              onChange={(event) =>
                onChange(
                  `${pointer}/currency`,
                  event.target.value === '' ? undefined : event.target.value.toUpperCase(),
                )
              }
            />
          </AxisField>
        </div>
      )}
    </Flex>
  )
}

/**
 * A label picker for each value of an enum field. Each change writes the whole `valueLabels` map, in the order of
 * `values`, and the last cleared label removes the key.
 */
function ValueLabels({ pointer, field, onChange }: { pointer: string; field: Content; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const values = Array.isArray(field.values) ? field.values.filter((item) => typeof item === 'string') : []
  const labels = isContent(field.valueLabels) ? field.valueLabels : {}

  function write(value: string, key: string | undefined) {
    const merged: Content = { ...labels }
    if (key === undefined) {
      delete merged[value]
    } else {
      setOwn(merged, value, { textKey: key })
    }
    const ordered: Content = {}
    for (const name of [...values, ...Object.keys(merged)]) {
      if (Object.hasOwn(merged, name) && !Object.hasOwn(ordered, name)) {
        setOwn(ordered, name, merged[name])
      }
    }
    onChange(pointer, Object.keys(ordered).length === 0 ? undefined : ordered)
  }

  return (
    <AxisFieldGroup {...groupTexts(t, 'fields.valueLabels')}>
      <Flex vertical gap="small">
        {values.map((value) => {
          const name = `${t('shell.studio.entity.valueLabel')} ${value}`
          const current = labels[value]
          const key = isContent(current) && typeof current.textKey === 'string' ? current.textKey : undefined
          return (
            <div key={value} data-testid={`studio-field-${pointer}/${escapePointerSegment(value)}`}>
              <AxisField label={name} help="">
                {names === undefined ? (
                  <Input
                    value={text(key)}
                    onChange={(event) => write(value, event.target.value === '' ? undefined : event.target.value)}
                  />
                ) : (
                  <ReferencePicker
                    name={name}
                    value={key}
                    required={false}
                    options={referenceOptions(names, 'text', undefined, pointer)}
                    onChange={(next) => write(value, next)}
                  />
                )}
              </AxisField>
            </div>
          )
        })}
      </Flex>
    </AxisFieldGroup>
  )
}

/**
 * The indexes of the entity as rows of a name, a field picker and a Unique choice. The picker offers the fields that have a
 * column and writes the picked names in order. Every change writes the whole `indexes` array, and an empty list
 * removes the key.
 */
const Indexes = memo(function Indexes({
  indexes,
  fields,
  readOnly,
  layer,
  onChange,
}: {
  indexes: unknown
  fields: unknown[]
  readOnly: boolean
  layer: DesignerLayer | undefined
  onChange: ChangeHandler
}) {
  const t = useText()

  if (indexes !== undefined && !Array.isArray(indexes)) {
    return (
      <SectionPart testName="indexes" heading={sectionHeading(t, 'indexes')}>
        <div data-testid="studio-field-/indexes">
          <JsonOnly />
        </div>
      </SectionPart>
    )
  }
  const list: unknown[] = indexes ?? []
  // A child collection, references and file field has no column of its own, so an index cannot name it.
  const choices = fields.flatMap((field) =>
    isContent(field) &&
    typeof field.name === 'string' &&
    field.name !== '' &&
    !['child-collection', 'references', 'file'].includes(text(field.type))
      ? [field.name]
      : [],
  )

  function write(next: unknown[]) {
    onChange('/indexes', next.length === 0 ? undefined : next)
  }

  function move(index: number, offset: number) {
    const next = [...list]
    ;[next[index], next[index + offset]] = [next[index + offset], next[index]]
    write(next)
  }

  return (
    <SectionPart testName="indexes" heading={sectionHeading(t, 'indexes')}>
      <div data-testid="studio-field-/indexes">
        <Flex vertical gap="small">
          {list.map((index, position) => {
            const pointer = `/indexes/${position}`
            const content = isContent(index) ? index : {}
            return (
              <Flex key={position} vertical gap="small" data-testid={`studio-field-${pointer}`}>
                <Flex gap="small" align="center">
                  <Typography.Text code data-testid={`studio-field-${pointer}/name`}>
                    {text(content.name)}
                  </Typography.Text>
                  {layer && <LayerMarkTag layer={layer} list="/indexes" index={position} name={text(content.name)} />}
                </Flex>
                <div data-testid={`studio-field-${pointer}/fields`} style={{ minWidth: 240 }}>
                  <AxisField {...fieldTexts(t, 'indexes.fields')} required>
                    <Select
                      mode="multiple"
                      virtual={false}
                      style={{ width: '100%' }}
                      value={
                        Array.isArray(content.fields) ? content.fields.filter((item) => typeof item === 'string') : []
                      }
                      options={choices.map((name) => ({ value: name, label: name }))}
                      onChange={(next: string[]) => onChange(`${pointer}/fields`, next)}
                    />
                  </AxisField>
                </div>
                <div data-testid={`studio-field-${pointer}/unique`}>
                  <AxisCheckboxField
                    {...fieldTexts(t, 'indexes.unique')}
                    checked={content.unique === true}
                    onChange={(checked) => onChange(`${pointer}/unique`, checked ? true : undefined)}
                  />
                </div>
                {readOnly ? null : (
                  <Flex gap="small">
                    {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                    <Button disabled={position === 0} onClick={() => move(position, -1)}>
                      {t('shell.studio.entity.moveUp')}
                    </Button>
                    {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                    <Button disabled={position === list.length - 1} onClick={() => move(position, 1)}>
                      {t('shell.studio.entity.moveDown')}
                    </Button>
                    {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                    <Button danger onClick={() => write(list.filter((_, other) => other !== position))}>
                      {t('shell.studio.entity.removeIndex')}
                    </Button>
                  </Flex>
                )}
              </Flex>
            )
          })}
          {readOnly ? null : (
            <div>
              {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
              <Button onClick={() => write([...list, { name: uniqueStepName(list, 'index'), fields: [] }])}>
                {t('shell.studio.entity.addIndex')}
              </Button>
            </div>
          )}
          <RemovedRows layer={layer} list="/indexes" />
        </Flex>
      </div>
    </SectionPart>
  )
})

/** The validations of the entity as rows of a name, an expression, a message key and a field. */
const Validations = memo(function Validations({
  validations,
  fieldNames,
  readOnly,
  layer,
  onChange,
}: {
  validations: unknown
  fieldNames: string[]
  readOnly: boolean
  layer: DesignerLayer | undefined
  onChange: ChangeHandler
}) {
  const t = useText()

  if (validations !== undefined && !Array.isArray(validations)) {
    return (
      <SectionPart testName="validations" heading={sectionHeading(t, 'validations')}>
        <div data-testid="studio-field-/validations">
          <JsonOnly />
        </div>
      </SectionPart>
    )
  }
  const list: unknown[] = validations ?? []

  return (
    <SectionPart testName="validations" heading={sectionHeading(t, 'validations')}>
      <div data-testid="studio-field-/validations">
        <Flex vertical gap="small">
          {list.map((validation, index) => (
            <ValidationRow
              key={index}
              index={index}
              validation={validation}
              fieldNames={fieldNames}
              readOnly={readOnly}
              layer={layer}
              onChange={onChange}
            />
          ))}
          {readOnly ? null : (
            <div>
              {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
              <Button
                onClick={() =>
                  onChange('/validations', [
                    ...list,
                    { name: uniqueStepName(list, 'validation'), expression: '', message: { textKey: '' }, field: '' },
                  ])
                }
              >
                {t('shell.studio.entity.addValidation')}
              </Button>
            </div>
          )}
          <RemovedRows layer={layer} list="/validations" />
        </Flex>
      </div>
    </SectionPart>
  )
})

/** One validation of the entity, which draws again only when its own validation or the field names change. */
const ValidationRow = memo(function ValidationRow({
  index,
  validation,
  fieldNames,
  readOnly,
  layer,
  onChange,
}: {
  index: number
  validation: unknown
  fieldNames: string[]
  readOnly: boolean
  layer: DesignerLayer | undefined
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/validations/${index}`
  const content = isContent(validation) ? validation : {}
  const expression = fieldTexts(t, 'validations.expression')
  return (
    <Flex vertical gap="small" data-testid={`studio-field-${pointer}`}>
      <Flex gap="small" align="center">
        <Typography.Text code data-testid={`studio-field-${pointer}/name`}>
          {text(content.name)}
        </Typography.Text>
        {layer && <LayerMarkTag layer={layer} list="/validations" index={index} name={text(content.name)} />}
      </Flex>
      <div data-testid={`studio-field-${pointer}/expression`} style={{ minWidth: 240 }}>
        <AxisField {...expression} required>
          <Expression
            pointer={`${pointer}/expression`}
            name={expression.label}
            value={text(content.expression)}
            required
            readOnly={readOnly}
            onChange={onChange}
          />
        </AxisField>
      </div>
      <Picker
        pointer={`${pointer}/message/textKey`}
        {...fieldTexts(t, 'validations.message')}
        kind="text"
        value={isContent(content.message) ? content.message.textKey : undefined}
        required
        onPick={(next) => onChange(`${pointer}/message/textKey`, next ?? '')}
      />
      <Picker
        pointer={`${pointer}/field`}
        {...fieldTexts(t, 'validations.field')}
        options={fieldNames}
        value={content.field}
        required
        onPick={(next) => onChange(`${pointer}/field`, next ?? '')}
      />
      {readOnly ? null : (
        <AxisButton role="destructive" onClick={() => onChange(pointer, undefined)}>
          {t('shell.studio.entity.removeValidation')}
        </AxisButton>
      )}
    </Flex>
  )
})
