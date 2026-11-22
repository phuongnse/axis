import { DeleteOutlined } from '@ant-design/icons'
import { ConfigProvider, Flex, Input, InputNumber, Select, Typography, DatePicker } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisActionRow, AxisButton } from '../platform/AxisButton'
import { StatusTag } from '../platform/StatusTag'
import dayjs from 'dayjs'
import { memo, useCallback, useContext, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { AxisCheckboxField } from '../platform/AxisCheckboxField'
import { AxisChoiceField } from '../platform/AxisChoiceField'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { AxisFieldRow } from '../platform/AxisFieldRow'
import { AxisTable, RowActions, type AxisColumn } from '../platform/AxisTable'
import { datePickerChevrons } from '../platform/datePickerChevrons'
import { EmptyDash } from '../platform/EmptyValue'
import { FilledText } from '../platform/FilledText'
import { booleanText } from '../platform/formatValue'
import {
  changeFieldType,
  editableSettings,
  fieldSettings,
  fieldTypes,
  valueSourceOf,
  valueSourcesOf,
  withValueSource,
  type FieldSetting,
  type FieldType,
  type ValueSource,
} from '../platform/entityFields'
import type { FieldOrigin, FieldWarning } from '../platform/entityMigrations'
import { escapePointerSegment, setAt } from '../platform/jsonPatch'
import { SidePanel } from '../platform/SidePanel'
import { focusFirstError } from '../platform/focusFirstError'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { ContentInsetContext } from '../platform/contentInsetContext'
import { withoutModulePrefix, type StudioNames } from '../platform/studio'
import { studioResourceAddress } from '../platform/studioAddress'
import { labelText, referenceOptions, StudioNamesContext, type ReferenceOption } from '../platform/studioNames'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useTableView } from '../platform/useTableView'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { type DesignerLayer, LayerMarkTag, RemovedRows } from './LayerMarkViews'
import { PlatformContext } from './context'
import { Expression } from './ProcessFields'
import { uniqueStepName } from './processPalette'
import { ReferencePicker } from './ReferencePicker'
import { fieldElement } from './studioFieldElement'
import type { ChangeHandler } from './SchemaForm'
import { setOwn } from '../platform/ownProperty'

type Content = Record<string, unknown>

/**
 * The groups of the field drawer after General, in the order it shows them, each with the settings it holds, see
 * docs/reference/entity-designer.md#the-drawer-and-its-groups. A pair shares one row. The type's group is titled by the
 * type, and a group with no setting the field's type takes is left out.
 */
const drawerGroups: {
  name: 'type' | 'value' | 'validation' | 'searchPrivacy'
  settings: (FieldSetting | [FieldSetting, FieldSetting])[]
}[] = [
  { name: 'type', settings: ['target', 'values', 'valueLabels', 'currency', ['precision', 'scale'], 'maxLength'] },
  // Proposed, #3264: Required and Unique sit in Value, right under the way the value comes from, as they hold for an
  // entered value, so they show near the top as other platforms show them.
  { name: 'value', settings: ['required', 'unique', 'default', 'userId', 'expression', 'sequence', 'match'] },
  { name: 'validation', settings: ['minLength', ['min', 'max'], 'maxSize', 'accept'] },
  { name: 'searchPrivacy', settings: ['searchable', 'sensitive', 'exactLookup'] },
]

/** The titles of a pair's row, by the pair's first setting. */
const pairTitles = { precision: 'fields.precisionScale', min: 'fields.limits' } as const

/** No field names: a validation drawn at its field has no field picker. */
const noFieldNames: string[] = []

/** Joins the field names into one text that tells whether the list changed. */
const nameSeparator = '\u0000'

/** Joins the parts of one field in such a text. */
const partSeparator = '\u0001'

/** A field that a matched reference may take its value from: its name, its type and, for a reference, its target. */
interface MatchSource {
  name: string
  type: string
  target: string
}

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
 * the field's inputs in groups, and the validations at the field, see docs/reference/entity-designer.md#field-drawer,
 * docs/reference/entity-designer.md#the-drawer-and-its-groups and docs/reference/entity-designer.md#fields.
 * Validations is rows of a name, an
 * expression, a message key and a field. A field can be
 * added, removed and moved, and a type change drops the settings the new type does not take. A computed field's
 * `expression` and each validation's expression use the expression editor, and so does a field's `default`, which has
 * no field in scope. A field's settings also include `min` and `max` in the type of the field, `minLength` and
 * `searchable`. The drawer's Value group is one choice of where the value comes from, and only the chosen way's settings
 * follow, see docs/reference/entity-designer.md#settings. The Indexes section, after Validations, adds, moves and removes the entity's `indexes`, each a name, a
 * list of the entity's fields and a Unique choice. The name of an index or a validation is shown as plain text, and an added one gets `index1` or
 * `validation1`, the first name its list does not use. The designer edits the draft through `onChange`, so a key it
 * does not show, such as `id`, `kind` or an unknown key on a field, stays as it is. Every input has the id
 * `studio-field-<pointer>`, and each row of the table has the id of its field. A diagnostic at a setting inside a field
 * opens that field's drawer with focus at the setting, see docs/reference/entity-designer.md#diagnostics, and `path` is
 * that diagnostic's path, or none. Clearing an optional value
 * removes its key. With `application`, the name of the application, a reference's target is a link to its entity, see
 * docs/reference/entity-designer.md#fields. With `readOnly`, as for an entity of a module, every input is off, the label
 * is its plain text key, and there are no buttons that
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
  application,
  path,
  readOnly = false,
  origins,
  warnings,
  onOriginsChange,
  layer,
}: {
  value: Content
  onChange: ChangeHandler
  application: string
  path?: string | null
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
                <LabelTexts pointer="/description" value={value.description} onChange={onChange} multiline />
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
          application={application}
          path={path}
          readOnly={readOnly}
          origins={origins}
          warnings={warnings}
          onOriginsChange={onOriginsChange}
          layer={layer}
          validations={value.validations}
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

/** A field's name, or `null` where the field has none, as an address's `field` names it. */
function fieldName(field: unknown): string | null {
  return text(isContent(field) ? field.name : undefined) || null
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

/**
 * The property names a field shows as tags: the way its value comes from, which for an entered value is User or Default
 * when it has one, then Searchable and Sensitive.
 */
function fieldProperties(field: unknown): string[] {
  if (!isContent(field)) {
    return []
  }
  const source = valueSourceOf(field)
  const way =
    source !== 'entered'
      ? source
      : field.userId === true
        ? 'user'
        : typeof field.default === 'string'
          ? 'default'
          : null
  return [...(way ? [way] : []), ...['searchable', 'sensitive'].filter((name) => field[name] === true)]
}

/** The Fields table keeps no data source parameter in the address, only its view. */
const noParameters: string[] = []

/**
 * The fields of the entity as a table, in the Fields section, with Add field on the right of its heading. A row reads the
 * field's name as a link with its layer mark, its label, its type in words, Required and Unique as Yes or No, and its
 * properties as tags, see docs/reference/entity-designer.md#fields. The name opens the field drawer, which holds the
 * field's inputs, see docs/reference/entity-designer.md#field-drawer. Every cell is a memo component of stable
 * props, so a key in one row draws that row's cells and no other. Only the row has an id in the table, and the drawer
 * has the ids of its inputs. The table has a search over Name and Label, as the Label cell shows it, kept in the
 * address as `fields.search`. It also has a Filter on Name, Label, Type, Required and Unique, kept as `fields.filter`.
 * A filter only hides rows: every row shows on one page, an edit to a hidden row stays in the draft, and
 * a field whose origin is `null`, one added since the last save, stays shown. Add field opens the new field's drawer.
 * Move up and Move down keep the open drawer on its field at the field's new index, and Remove closes the drawer only
 * when it removes that field, see docs/reference/entity-designer.md#field-drawer. A read-only designer has no "…" menu,
 * see docs/reference/entity-designer.md#fields.
 *
 * The open drawer's field is in the address as `field=<name>`, see docs/reference/studio.md#route-and-address. Opening
 * and closing it add a history entry, and a rename or a removal replaces the entry. A `field` that names no field opens
 * nothing and is dropped. `path` is a diagnostic's path: one inside a field, such as `/fields/2/maxLength`, opens that
 * field's drawer with focus at the setting, see docs/reference/entity-designer.md#diagnostics.
 */
const Fields = memo(function Fields({
  fields,
  application,
  path,
  readOnly,
  origins,
  warnings,
  onOriginsChange,
  layer,
  validations,
  onChange,
}: {
  fields: unknown[] | undefined
  application: string
  path: string | null | undefined
  readOnly: boolean
  origins: FieldOrigin[] | undefined
  warnings: Map<number, FieldWarning> | undefined
  onOriginsChange: ((next: FieldOrigin[], touched?: string) => void) | undefined
  layer: DesignerLayer | undefined
  validations: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const [searchParams, setSearchParams] = useSearchParams()
  const addressed = searchParams.get('field')
  // The row of the field whose drawer is open or leaving. The drawer keeps its field while it closes, and shows as
  // closed when its row is gone, as after an Undo.
  const [drawer, setDrawer] = useState<{ index: number; open: boolean } | null>(null)
  const shown = drawer !== null && fields !== undefined && drawer.index < fields.length ? drawer.index : null
  // The name of the open drawer's field, or `null` while the drawer is closed or its field has no name.
  const drawerName = shown !== null && drawer?.open === true && fields !== undefined ? fieldName(fields[shown]) : null
  // The address and the path that the drawer last followed. A change of either is followed while drawing, as the editor
  // follows its path, so the drawer never draws the field of a stale address.
  const [seenAddress, setSeenAddress] = useState<string | null | undefined>(undefined)
  const [seenPath, setSeenPath] = useState<string | null | undefined>(undefined)
  // The path inside the field that the drawer focuses once it has opened.
  const [focusPath, setFocusPath] = useState<string | null>(null)
  // The name of the open field that the address holds, as this component last wrote it or followed it.
  const written = useRef<string | null | undefined>(undefined)
  // The row whose name takes focus once its drawer has closed, set by Close and not by a removal or by Back.
  const returnFocus = useRef<number | null>(null)
  // What the handlers below read when they are pressed, so they stay the same between draws.
  const latest = useRef({ fields, origins, onOriginsChange, onChange, searchParams, setSearchParams, shown })
  useLayoutEffect(() => {
    latest.current = { fields, origins, onOriginsChange, onChange, searchParams, setSearchParams, shown }
  })
  const names = useContext(StudioNamesContext)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { view, setView } = useTableView(fieldTableFields, recordPageSize, noParameters, true, true, false, 'fields.')

  /**
   * Writes the open field's name as `field` into the address, or removes `field` for no name, and remembers the name.
   * A write that changes nothing adds no history entry.
   */
  const writeField = useCallback((name: string | null, replace: boolean) => {
    const { searchParams, setSearchParams } = latest.current
    written.current = name
    const next = new URLSearchParams(searchParams)
    if (name === null) {
      next.delete('field')
    } else {
      next.set('field', name)
    }
    if (next.toString() !== searchParams.toString()) {
      setSearchParams(next, { replace, preventScrollReset: true })
    }
  }, [])

  /** Opens the drawer of the field at `index`, and adds its name to the address as a history entry. */
  /**
   * Proposed, #3264: the new field that Add field opens in the side panel, held there and not in the draft until Add
   * field puts it in, so Cancel adds nothing. `null` while no new field is open.
   */
  const [staged, setStaged] = useState<Record<string, unknown> | null>(null)
  // What Add field found wrong with the new field, dropped whenever its panel goes.
  const [stagedErrors, setStagedErrors] = useState<{ label?: string; name?: string }>({})

  const openDrawer = useCallback(
    (index: number) => {
      // One side panel shows at a time, so a field opened from the table takes the place of a new field's panel.
      setStaged(null)
      setStagedErrors({})
      setDrawer({ index, open: true })
      writeField(fieldName(latest.current.fields?.[index]), false)
    },
    [writeField],
  )

  const closeDrawer = useCallback(() => {
    setDrawer((current) => current && { ...current, open: false })
    returnFocus.current = latest.current.shown
    writeField(null, false)
  }, [writeField])

  const clearFocusPath = useCallback(() => setFocusPath(null), [])

  // The drawer follows an address that changed from outside, such as Back, a link or a reload. An address that names
  // the drawer's field already, or that is no name while the drawer's field has none, leaves the drawer as it is.
  if (fields !== undefined && addressed !== seenAddress) {
    setSeenAddress(addressed)
    if (addressed !== drawerName) {
      const index = addressed === null ? -1 : fields.findIndex((field) => fieldName(field) === addressed)
      setDrawer(index === -1 ? (current) => current && { ...current, open: false } : { index, open: true })
    }
  }

  // A path inside a field opens that field's drawer at the setting, once for each path. The drawer's name then goes
  // into the address with a replace, see the effect below.
  if (fields !== undefined && path !== seenPath) {
    setSeenPath(path)
    const match = path ? /^\/fields\/(\d+)\/.+/.exec(path) : null
    if (path && match && Number(match[1]) < fields.length) {
      setDrawer({ index: Number(match[1]), open: true })
      setFocusPath(path)
    }
  }

  // A `field` that the address holds from outside is the name this component follows. A name that no field has is
  // dropped from the address with a replace, so it opens nothing and leaves no history entry.
  useEffect(() => {
    if (fields === undefined || addressed === written.current) {
      return
    }
    written.current = addressed
    if (addressed !== null && !fields.some((field) => fieldName(field) === addressed)) {
      writeField(null, true)
    }
  }, [addressed, fields, writeField])

  // The open drawer's name is the address's `field`. A rename, an Undo or the removal of the open field changes the
  // address with a replace, and an edit that names the drawer's field already writes nothing.
  useEffect(() => {
    if (fields !== undefined && drawerName !== written.current) {
      writeField(drawerName, true)
    }
  }, [drawerName, fields, writeField])

  // Focus returns to the row's name once its drawer has closed, see docs/reference/entity-designer.md#field-drawer.
  const drawerOpen = drawer?.open === true
  useEffect(() => {
    if (drawerOpen || returnFocus.current === null) {
      return
    }
    // The name is the row's link, or its button while it has no name; the row's drag handle comes before it.
    document
      .querySelector<HTMLElement>(`[data-testid="studio-field-/fields/${returnFocus.current}"] [data-field-name]`)
      ?.focus()
    returnFocus.current = null
  }, [drawerOpen])

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

  // The fields a matched reference may take its value from, by row: a named field that is neither sensitive, matched nor
  // computed, else `null`. The drawer filters them by the Find by field's type.
  const matchSignature = (fields ?? [])
    .map((other) =>
      isContent(other) &&
      typeof other.name === 'string' &&
      other.name !== '' &&
      other.sensitive !== true &&
      !isContent(other.match) &&
      typeof other.expression !== 'string'
        ? [other.name, text(other.type), text(other.target)].join(partSeparator)
        : '',
    )
    .join(nameSeparator)
  const matchSources = useMemo(
    () =>
      matchSignature.split(nameSeparator).map((row): MatchSource | null => {
        const [name, type, target] = row.split(partSeparator)
        return row === '' ? null : { name, type, target }
      }),
    [matchSignature],
  )

  // Moves the field at `index` by `offset` rows, one for Move up and Move down, or many for a drag.
  const onMove = useCallback((index: number, offset: number) => {
    const { fields, origins, onOriginsChange, onChange } = latest.current
    const to = index + offset
    // The drawer's field moves with it, and a closed drawer keeps its index in step, so it never opens on another field.
    setDrawer((current) => current && { ...current, index: movedIndex(current.index, index, to) })
    onChange('/fields', moved(fields!, index, to))
    if (onOriginsChange) {
      onOriginsChange(moved(rowOrigins(fields!, origins), index, to))
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
    // An open field's panel gives way to the new field's, as one side panel shows at a time.
    // The new field's panel takes the place of an open field's, and the focus goes into it, not back to the row.
    if (latest.current.shown !== null) {
      setDrawer((current) => current && { ...current, open: false })
      writeField(null, false)
    }
    setStaged({ name: '', type: 'text' })
    setStagedErrors({})
  }

  /** A change in the new field's panel, at a pointer under `/fields/<its row>`, goes to the field the panel holds. */
  function changeStaged(pointer: string, value: unknown) {
    setStagedErrors({})
    const prefix = `/fields/${fields!.length}`
    if (pointer === prefix) {
      setStaged(isContent(value) ? value : {})
    } else if (pointer.startsWith(`${prefix}/`)) {
      setStaged((current) => setAt(current ?? {}, pointer.slice(prefix.length), value) as Record<string, unknown>)
    }
  }

  const labelDrafts = useContext(LabelTextsContext)

  /**
   * Proposed, #3264: Add field puts the new field into the draft only with a label, in words in any language, and a
   * name that starts with a letter, has only letters and digits, and no other field has. Otherwise the panel stays open
   * with the errors under the label and the name.
   */
  function commitStaged() {
    const key = isContent(staged?.label) ? text(staged.label.textKey) : ''
    const problem = nameProblem(text(staged?.name), namesExcept(fields!, null))
    const errors = {
      ...(labelled(labelDrafts?.texts[key]) ? {} : { label: t('shell.studio.entity.fieldPanel.labelRequired') }),
      ...(problem === undefined ? {} : { name: t(problem) }),
    }
    if (errors.label !== undefined || errors.name !== undefined) {
      setStagedErrors(errors)
      return
    }
    onChange('/fields', [...fields!, staged])
    onOriginsChange?.([...rowOrigins(fields!, origins), null])
    setStaged(null)
    setStagedErrors({})
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
      searchText: ({ field }) => text(isContent(field) ? field.name : undefined),
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
      // The search reads the label as the Label cell shows it, so a person finds what they read in the row.
      searchText: ({ field }) => fieldLabel(field, names, locale),
      render: (_, { field }) => <LabelCell field={field} />,
    },
    {
      key: 'type',
      kind: 'freeText',
      title: t('shell.studio.entity.fieldType'),
      filter: {
        kind: 'enum',
        value: ({ field }) => text(isContent(field) ? field.type : undefined) || null,
        options: fieldTypeChoices(t).map(({ value, label }) => ({ value, label })),
      },
      render: (_, { field }) => <TypeCell application={application} field={field} />,
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
          <AxisActionRow outlined>
            <AxisButton role="secondary" onClick={addField}>
              {t('shell.studio.entity.addField')}
            </AxisButton>
          </AxisActionRow>
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
            reorder={readOnly ? undefined : { onMove: (from, to) => onMove(from, to - from) }}
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
        open={drawerOpen && staged === null}
        focusPath={focusPath}
        onFocused={clearFocusPath}
        field={shown === null ? undefined : fields[shown]}
        otherNames={namesExcept(fields, shown)}
        currencyNames={
          shown !== null && isContent(fields[shown]) && editableSettings(fields[shown]).includes('currency')
            ? currencyNames
            : undefined
        }
        matchSources={
          shown !== null && isContent(fields[shown]) && editableSettings(fields[shown]).includes('match')
            ? matchSources
            : undefined
        }
        readOnly={readOnly}
        warning={shown === null ? undefined : warnings?.get(shown)}
        validations={validations}
        layer={layer}
        onChange={onChange}
        onClose={closeDrawer}
      />
      {staged !== null && (
        <FieldDrawer
          index={fields.length}
          open
          focusPath={null}
          onFocused={clearFocusPath}
          field={staged}
          otherNames={namesExcept(fields, null)}
          currencyNames={editableSettings(staged).includes('currency') ? currencyNames : undefined}
          matchSources={editableSettings(staged).includes('match') ? matchSources : undefined}
          readOnly={false}
          warning={undefined}
          validations={undefined}
          layer={layer}
          onChange={changeStaged}
          onClose={() => {
            setStaged(null)
            setStagedErrors({})
          }}
          onAdd={commitStaged}
          errors={stagedErrors}
        />
      )}
    </SectionPart>
  )
})

/**
 * The field types in the order Add field offers them: text and numbers, then yes or no and dates, then a choice, the
 * types that point to another entity, and a file. Proposed, see #3264.
 */
const fieldTypeChoiceOrder: readonly FieldType[] = [
  'text',
  'integer',
  'decimal',
  'money',
  'boolean',
  'date',
  'date-time',
  'enum',
  'reference',
  'references',
  'child-collection',
  'file',
]

/** The field types as choices, each its name in words and a line that says what it holds. Proposed, see #3264. */
function fieldTypeChoices(t: (key: string) => string) {
  return fieldTypeChoiceOrder.map((type) => ({
    value: type,
    label: t(`shell.studio.entity.type.${type}`),
    help: t(`shell.studio.entity.type.${type}.help`),
  }))
}

/**
 * A field's name made from its label, such as "Amount with tax" to `amountWithTax`: its letters without their marks,
 * "đ" as "d", its words joined in camel case, starting with a letter, as a field's name must.
 */
function nameFromLabel(label: string): string {
  const words = label
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .replace(/Đ/g, 'D')
    .split(/[^A-Za-z0-9]+/)
    .filter((word) => word !== '')
  const joined = words
    .map((word, position) =>
      position === 0 ? word.toLowerCase() : word.charAt(0).toUpperCase() + word.slice(1).toLowerCase(),
    )
    .join('')
  return joined.replace(/^[0-9]+/, '')
}

/**
 * Proposed, #3264: what is wrong with a field's name, as the text key of its error, or `undefined` when nothing is: it
 * is empty, it does not start with a letter or has more than letters and digits, or another field has it, ignoring case.
 * Add field and an open field's panel check a name the same way.
 */
function nameProblem(name: string, others: readonly string[]): string | undefined {
  if (name === '') return 'shell.studio.entity.fieldPanel.nameRequired'
  if (!/^[A-Za-z][A-Za-z0-9]*$/.test(name)) return 'shell.studio.entity.fieldPanel.nameInvalid'
  if (others.some((other) => other.toLowerCase() === name.toLowerCase()))
    return 'shell.studio.entity.fieldPanel.nameTaken'
  return undefined
}

/** The names of the fields, but the one at `except`, which a field's name may not take. */
function namesExcept(fields: readonly unknown[], except: number | null): string[] {
  return fields.flatMap((field, position) => {
    const name = position === except ? null : fieldName(field)
    return name ? [name] : []
  })
}

/** Whether a label's texts have words in at least one language. */
function labelled(texts: Record<string, string> | undefined): boolean {
  return Object.values(texts ?? {}).some((value) => value.trim() !== '')
}

/** `list` with its item at `from` moved to `to`, the items between them shifting by one. */
function moved<T>(list: readonly T[], from: number, to: number): T[] {
  const next = [...list]
  const [item] = next.splice(from, 1)
  next.splice(to, 0, item)
  return next
}

/** Where the item at `index` ends when the item at `from` moves to `to`. */
function movedIndex(index: number, from: number, to: number): number {
  if (index === from) return to
  if (from < index && index <= to) return index - 1
  if (to <= index && index < from) return index + 1
  return index
}

/** A field type in the Type list: its name, and under it what it holds, in muted small text. */
function TypeOption({ label, help }: { label: string; help: string }) {
  const { colour } = useDesignTokens()
  return (
    <div style={{ whiteSpace: 'normal' }}>
      <div>{label}</div>
      <div style={{ fontSize: 12, lineHeight: '20px', fontWeight: 'normal', color: colour.textMuted }}>{help}</div>
    </div>
  )
}

/** The query of the open resource with the drawer of the field `name` open, and no path. */
function fieldAddress(current: URLSearchParams, name: string): URLSearchParams {
  const next = new URLSearchParams(current)
  next.delete('path')
  next.set('field', name)
  return next
}

/**
 * The name of a field's row as a link that opens the field drawer, with the mark of a customization layer. Proposed,
 * #4902: it is a real link to the field's address, `&field=<name>`, see docs/reference/studio.md#route-and-address, so
 * it opens in a new tab and its address can be copied. A plain click opens the drawer here, as the address would. A field
 * with no name has no address, so its name is a subtle button.
 */
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
  const [searchParams] = useSearchParams()
  const name = text(isContent(field) ? field.name : undefined)
  return (
    <Flex gap="small" align="center" wrap>
      {/* A read-only designer turns its inputs off, but its name still opens the drawer. */}
      {name ? (
        <Link
          data-field-name
          to={{ search: `?${fieldAddress(searchParams, name)}` }}
          onClick={(event) => {
            // A click with a modifier or another button opens the address as the browser does, such as in a new tab.
            if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
              return
            }
            event.preventDefault()
            onOpen(index)
          }}
        >
          {name}
        </Link>
      ) : (
        <AxisButton role="subtle" size="small" data-field-name disabled={false} onClick={() => onOpen(index)}>
          {t('shell.studio.entity.unnamedField')}
        </AxisButton>
      )}
      {layer && <LayerMarkTag layer={layer} list="/fields" index={index} name={name} />}
    </Flex>
  )
})

/**
 * A field's label as the Label cell shows it: the text in the author's language, else its text key. It is '' where the
 * field has no label.
 */
function fieldLabel(field: unknown, names: StudioNames | undefined, locale: string): string {
  const label = isContent(field) ? field.label : undefined
  const key = (isContent(label) ? text(label.textKey) : '') || undefined
  return key === undefined ? '' : (names && labelText(names, key, locale)) || key
}

/** The label of a field's row in the author's language, else its text key, and the dash when it has none. */
const LabelCell = memo(function LabelCell({ field }: { field: unknown }) {
  const names = useContext(StudioNamesContext)
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const label = fieldLabel(field, names, locale)
  return label === '' ? <EmptyDash /> : label
})

/**
 * The type of a field in words. For a reference, references or child collection, the target's label fills in its
 * place, or the target's name where the entity has no label. The target is matched by name, ignoring letter case. A
 * target that is one of the application's entities is a link to it, at its module view for a module's entity. A name
 * the application does not have reads as written and is not a link. A type the designer does not know reads as written
 * too, see docs/reference/entity-designer.md#fields.
 */
/** The types that point to another entity, whose words name it. */
const targetTypes: readonly string[] = ['reference', 'references', 'child-collection']

const TypeCell = memo(function TypeCell({ field, application }: { field: unknown; application: string }) {
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
  if (!targetTypes.includes(type)) {
    return t(`shell.studio.entity.type.${type}`)
  }
  const target = text(isContent(field) ? field.target : undefined)
  if (target === '') {
    return <FilledText text={t(`shell.studio.entity.typeOf.${type}`)} parts={{ target: <EmptyDash /> }} />
  }
  const entity = names?.entities.find((other) => other.name.toLowerCase() === target.toLowerCase())
  if (names === undefined || entity === undefined) {
    return <FilledText text={t(`shell.studio.entity.typeOf.${type}`)} parts={{ target }} />
  }
  const label = labelText(names, entity.labelKey, locale) || withoutModulePrefix(entity.name, entity.module)
  return (
    <FilledText
      text={t(`shell.studio.entity.typeOf.${type}`)}
      parts={{ target: <Link to={studioResourceAddress(application, entity.id)}>{label}</Link> }}
    />
  )
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
        <StatusTag key={name} tone="neutral">
          {t(`shell.studio.entity.property.${name}`)}
        </StatusTag>
      ))}
    </Flex>
  )
})

/** The inputs that take focus in a drawer's field, in the order a path's element is searched for one. */
const focusableInput = 'input, textarea, [contenteditable="true"]'

/**
 * Moves focus into the drawer: to the input of the field at `path`, scrolled to, or to the drawer when the path shows no
 * input. Without a path it is the name input, unless the drawer is read-only, when it is the drawer. The drawer takes
 * focus when no input does, see docs/reference/actions-and-controls.md#fields.
 */
function focusInDrawer(root: HTMLElement, path: string | null, pointer: string, readOnly: boolean) {
  const target = path === null && readOnly ? undefined : fieldElement(root, path ?? `${pointer}/name`)
  if (path !== null) {
    target?.scrollIntoView?.({ block: 'center' })
  }
  const input = target?.querySelector<HTMLElement>(focusableInput)
  ;(input && !input.matches(':disabled') ? input : root).focus()
}

/**
 * The drawer of a field, beside the table and above the save bar, with no mask so the table stays clickable, see
 * docs/reference/entity-designer.md#field-drawer. From the top it holds the migration warning, then the groups General
 * (the field's name, label, type and help) and those of its settings, see
 * docs/reference/entity-designer.md#the-drawer-and-its-groups. Each is an input with its help and its id. Opening it
 * moves focus into it, at the name or, for a `focusPath` inside the field, at that setting. Its `focusPath` is reported
 * back through `onFocused` once focus has moved. Proposed, #3264: it is the platform's one side panel, `SidePanel`, in
 * the shell's right column from under the header to the footer, see docs/reference/shell.md#layout. An existing
 * field's edits go into the draft at once and Done closes the panel. A new field, `onAdd` given, is held by the panel
 * until Add field puts it into the draft; Cancel and Close add nothing.
 */
const FieldDrawer = memo(function FieldDrawer({
  onAdd,
  errors,
  otherNames,
  index,
  open,
  focusPath,
  onFocused,
  field,
  currencyNames,
  matchSources,
  readOnly,
  warning,
  validations,
  layer,
  onChange,
  onClose,
}: {
  index: number | null
  open: boolean
  focusPath: string | null
  onFocused: () => void
  field: unknown
  currencyNames: (string | null)[] | undefined
  matchSources: (MatchSource | null)[] | undefined
  readOnly: boolean
  warning: FieldWarning | undefined
  validations: unknown
  layer: DesignerLayer | undefined
  onChange: ChangeHandler
  onClose: () => void
  onAdd?: () => void
  /** Proposed, #3264: what Add field found wrong with a new field, under its label and its name. */
  errors?: { label?: string; name?: string }
  /** The names of the entity's other fields, which the field's name may not take. */
  otherNames: readonly string[]
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const content = isContent(field) ? field : {}
  const name = text(content.name)
  const type = text(content.type)
  const pointer = `/fields/${index}`
  const author = useContext(PlatformContext)?.current.locale ?? 'en'
  // A new field takes its name from its label until the name is changed by hand, as Dataverse and ServiceNow make a
  // column's name from its display name.
  const [nameTouched, setNameTouched] = useState(false)
  // The drawer's root, set once its content has drawn, so the focus effect below runs after the drawer has opened.
  const [root, setRoot] = useState<HTMLElement | null>(null)
  const labelTexts = useContext(LabelTextsContext)
  const labelPointer = `${pointer}/label`
  const labelKey = isContent(content.label) ? text(content.label.textKey) : ''
  // Proposed, #3264: an open field's label and name go into the draft only while they are valid, so the draft always
  // has both. While one is not, the panel shows what was typed with its error, and the draft keeps the last valid one.
  const [typed, setTyped] = useState<{ index: number | null; name?: string; label?: Record<string, string> }>({
    index,
  })
  // Another field, or the panel opened anew, starts with what the draft holds, dropping what was typed for the last one:
  // what was typed belongs to the open panel's field, and a closed panel has none.
  const typedFor = open ? index : null
  if (typed.index !== typedFor) {
    setTyped({ index: typedFor })
  }
  const mine = typed.index === typedFor ? typed : { index: typedFor }
  const shownName = mine.name ?? name
  const editing = onAdd === undefined
  const nameError = editing && mine.name !== undefined ? nameProblem(mine.name, otherNames) : undefined
  const labelError = editing && mine.label !== undefined && !labelled(mine.label)
  const namingTexts = useMemo(() => {
    if (labelTexts === null) {
      return labelTexts
    }
    if (!editing) {
      return nameTouched
        ? labelTexts
        : {
            ...labelTexts,
            onTextsChange: (at: string, key: string, texts: Record<string, string>) => {
              labelTexts.onTextsChange(at, key, texts)
              onChange(`${pointer}/name`, nameFromLabel(texts[author] ?? texts.en ?? ''))
            },
          }
    }
    return {
      ...labelTexts,
      texts: mine.label === undefined ? labelTexts.texts : { ...labelTexts.texts, [labelKey]: mine.label },
      onTextsChange: (at: string, key: string, texts: Record<string, string>) => {
        if (at === labelPointer && !labelled(texts)) {
          setTyped((current) => ({ ...(current.index === index ? current : { index }), label: texts }))
          return
        }
        setTyped((current) => ({ ...(current.index === index ? current : { index }), label: undefined }))
        labelTexts.onTextsChange(at, key, texts)
      },
    }
  }, [labelTexts, editing, nameTouched, onChange, pointer, author, mine.label, labelKey, labelPointer, index])
  const changeName = (next: string) => {
    setNameTouched(true)
    if (editing && nameProblem(next, otherNames) !== undefined) {
      setTyped((current) => ({ ...(current.index === index ? current : { index }), name: next }))
      return
    }
    setTyped((current) => ({ ...(current.index === index ? current : { index }), name: undefined }))
    onChange(`${pointer}/name`, next)
  }
  const labelErrorText = errors?.label ?? (labelError ? t('shell.studio.entity.fieldPanel.labelRequired') : undefined)
  // Done closes only a field whose label and name are valid, and otherwise takes the person to the first box in error.
  // Close puts back the last valid ones, which the draft holds.
  const done = () => {
    if (nameError === undefined && !labelError) {
      onClose()
    } else {
      focusFirstError(root)
    }
  }
  // Add field's errors take the person to the first box in error as they show.
  useEffect(() => {
    if (errors?.label !== undefined || errors?.name !== undefined) {
      focusFirstError(root)
    }
  }, [errors, root])
  const close = () => {
    setTyped({ index })
    onClose()
  }
  const wasOpen = useRef(false)
  const wantFocus = useRef(false)

  useEffect(() => {
    if (!open) {
      wasOpen.current = false
      wantFocus.current = false
      return
    }
    if (!wasOpen.current) {
      wasOpen.current = true
      wantFocus.current = true
    }
    if (root === null || !(wantFocus.current || focusPath !== null) || index === null) {
      return
    }
    wantFocus.current = false
    focusInDrawer(root, focusPath, pointer, readOnly)
    if (focusPath !== null) {
      onFocused()
    }
  }, [open, root, focusPath, index, pointer, readOnly, onFocused])

  return (
    open &&
    index !== null && (
      <SidePanel
        id="studio-field-panel"
        testId="studio-field-panel"
        title={onAdd ? t('shell.studio.entity.newField') : name || t('shell.studio.entity.unnamedField')}
        onClose={close}
        closeNameKey="shell.studio.entity.fieldPanel.close"
        actions={
          readOnly ? undefined : onAdd ? (
            <>
              <AxisButton role="secondary" onClick={close}>
                {t('shell.studio.cancel')}
              </AxisButton>
              <AxisButton role="main" onClick={onAdd}>
                {t('shell.studio.entity.addField')}
              </AxisButton>
            </>
          ) : (
            <AxisButton role="main" onClick={done}>
              {t('shell.studio.entity.fieldPanel.done')}
            </AxisButton>
          )
        }
      >
        {/* Proposed, #3264: the panel's parts are parted by rules that run from its edge to its scrollbar, as a page's
            parts are, see docs/reference/shell.md#layout. */}
        <ContentInsetContext.Provider value={spacing.sm}>
          {index !== null && (
            <Flex vertical gap="middle" data-testid="studio-field-drawer" key={index} ref={setRoot} tabIndex={-1}>
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
              <SectionParts>
                <SectionPart testName="field-general">
                  <AxisFieldGroup heading {...groupTexts(t, 'fields.group.general')}>
                    <Flex vertical gap="middle">
                      {/* Proposed, #3264: the label first, typed in words with its translations as the entity's label
                      is, then the name, which a new field takes from its label until the name is changed by hand. */}
                      <div data-testid={`studio-field-${pointer}/label`}>
                        <AxisFieldGroup {...groupTexts(t, 'fields.label')} required error={labelErrorText}>
                          <LabelTextsContext.Provider value={namingTexts}>
                            <LabelTexts
                              pointer={`${pointer}/label`}
                              value={content.label}
                              onChange={onChange}
                              invalid={labelErrorText !== undefined}
                            />
                          </LabelTextsContext.Provider>
                        </AxisFieldGroup>
                      </div>
                      <div data-testid={`studio-field-${pointer}/name`}>
                        <AxisField
                          {...fieldTexts(t, 'fields.name')}
                          required
                          error={errors?.name ?? (nameError === undefined ? undefined : t(nameError))}
                        >
                          <Input value={shownName} onChange={(event) => changeName(event.target.value)} />
                        </AxisField>
                      </div>
                      <div data-testid={`studio-field-${pointer}/type`}>
                        <AxisField {...fieldTexts(t, 'fields.type')} required>
                          <Select
                            showSearch
                            optionFilterProp="label"
                            virtual={false}
                            value={type === '' ? undefined : type}
                            // Proposed, #3264: the types by name in words, each with a line that says what it holds, as
                            // the choice of a new field's type is made here.
                            options={fieldTypeChoices(t)}
                            optionRender={({ data }) => <TypeOption label={data.label} help={data.help} />}
                            onChange={(next: string) => onChange(pointer, changeFieldType(content, next))}
                          />
                        </AxisField>
                      </div>
                      <div data-testid={`studio-field-${pointer}/help`}>
                        <AxisFieldGroup {...groupTexts(t, 'fields.helpText')}>
                          <LabelTexts pointer={`${pointer}/help`} value={content.help} onChange={onChange} />
                        </AxisFieldGroup>
                      </div>
                    </Flex>
                  </AxisFieldGroup>
                </SectionPart>
                <Settings
                  index={index}
                  field={content}
                  currencyNames={currencyNames}
                  matchSources={matchSources}
                  readOnly={readOnly}
                  validations={validations}
                  layer={layer}
                  onChange={onChange}
                />
              </SectionParts>
            </Flex>
          )}
        </ContentInsetContext.Provider>
      </SidePanel>
    )
  )
})

/**
 * The settings of one field that its type takes, in the groups the type's settings, Value, Validation, and Search and
 * privacy, each with its own input and id, see docs/reference/entity-designer.md#the-drawer-and-its-groups. A group with
 * nothing to show is left out, and a pair of settings shares one row. Value starts with one choice of the ways the type
 * takes, and only the chosen way's settings follow, see docs/reference/entity-designer.md#settings. A named field's Validation group also lists the
 * validations whose `field` is this field and adds one, see docs/reference/entity-designer.md#settings.
 */
function Settings({
  index,
  field,
  currencyNames,
  matchSources,
  readOnly,
  validations,
  layer,
  onChange,
}: {
  index: number
  field: Content
  currencyNames: (string | null)[] | undefined
  matchSources: (MatchSource | null)[] | undefined
  readOnly: boolean
  validations: unknown
  layer: DesignerLayer | undefined
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/fields/${index}`
  const taken = editableSettings(field)
  const type = text(field.type)
  const sources = valueSourcesOf(type)
  const name = text(field.name)
  // A validation's `field` must name a field, so a field with no name has none and adds none.
  const list: unknown[] | undefined =
    validations === undefined ? [] : Array.isArray(validations) ? validations : undefined
  const canAdd = name !== '' && list !== undefined && !readOnly
  const atField =
    name === '' || list === undefined
      ? []
      : list.flatMap((validation, position) =>
          isContent(validation) && validation.field === name ? [{ validation, position }] : [],
        )

  const settingTexts = (setting: FieldSetting) => fieldTexts(t, `fields.${setting}`)
  // Search and privacy is seldom changed, so it starts folded and says which of its settings are on.
  const [privacyOpen, setPrivacyOpen] = useState(false)
  const privacyOn = (['searchable', 'sensitive', 'exactLookup'] as const).filter((setting) => field[setting] === true)
  const privacyState =
    privacyOn.length === 0
      ? t('shell.studio.field.entity.fields.group.searchPrivacy.none')
      : privacyOn.map((setting) => settingTexts(setting).label).join(', ')

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

  /** The yes-or-no setting `setting`, a checkbox, as every yes-or-no setting of the drawer is. */
  function flag(setting: 'required' | 'unique' | 'userId' | 'sensitive' | 'exactLookup' | 'searchable') {
    return (
      <AxisCheckboxField
        {...settingTexts(setting)}
        checked={field[setting] === true}
        onChange={(checked) => {
          if (setting === 'sensitive' && !checked && Object.hasOwn(field, 'exactLookup')) {
            // Unticking Sensitive takes Exact lookup with it, as one change, because a field that is not sensitive may not
            // have it, even as false.
            const rebuilt: Content = {}
            for (const [key, value] of Object.entries(field)) {
              if (key !== 'exactLookup') {
                setOwn(rebuilt, key, key === 'sensitive' ? false : value)
              }
            }
            onChange(pointer, rebuilt)
            return
          }
          // Even a false exactLookup is refused on a field that is not sensitive, and a false userId on a field that is
          // not text, so unchecking removes them, as it does for searchable.
          onChange(
            `${pointer}/${setting}`,
            (setting === 'exactLookup' || setting === 'userId' || setting === 'searchable') && !checked
              ? undefined
              : checked,
          )
        }}
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
            required
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
    return (
      control && (
        <AxisField {...settingTexts(setting)} required={setting === 'expression'}>
          {control}
        </AxisField>
      )
    )
  }

  function drawn(setting: FieldSetting) {
    return setting === 'currency' ? (
      <CurrencySource
        key={setting}
        pointer={pointer}
        field={field}
        currencyFields={(currencyNames ?? []).flatMap((name, position) =>
          position !== index && name !== null ? [name] : [],
        )}
        onChange={onChange}
      />
    ) : setting === 'match' ? (
      <MatchedSource
        key={setting}
        pointer={pointer}
        index={index}
        field={field}
        matchSources={matchSources}
        onChange={onChange}
      />
    ) : setting === 'target' || setting === 'sequence' ? (
      <Picker
        key={setting}
        pointer={`${pointer}/${setting}`}
        {...settingTexts(setting)}
        kind={setting === 'target' ? 'entity' : 'sequence'}
        value={field[setting]}
        required
        // An emptied sequence stays an empty key, so the field keeps its way and Validate reports it.
        onPick={(next) => onChange(`${pointer}/${setting}`, next ?? '')}
      />
    ) : (
      <div key={setting} data-testid={`studio-field-${pointer}/${setting}`}>
        {setting === 'required' ||
        setting === 'unique' ||
        setting === 'userId' ||
        setting === 'sensitive' ||
        setting === 'exactLookup' ||
        setting === 'searchable'
          ? flag(setting)
          : setting === 'valueLabels'
            ? input(setting)
            : labelled(setting)}
      </div>
    )
  }

  /** A pair that the field's type takes whole shares a row. A type that takes one of them draws that one alone. */
  function drawnEntry(entry: FieldSetting | [FieldSetting, FieldSetting]) {
    const entered = (typeof entry === 'string' ? [entry] : entry).filter((setting) => taken.includes(setting))
    if (typeof entry === 'string' || entered.length < 2) {
      return entered.map(drawn)
    }
    return [
      <AxisFieldRow key={entry[0]} {...groupTexts(t, pairTitles[entry[0] as keyof typeof pairTitles])}>
        {entered.map(drawn)}
      </AxisFieldRow>,
    ]
  }

  // Each group is a part of the panel, so a rule parts it from the one before, the General group's included.
  return (
    <SectionParts>
      {drawerGroups.map(({ name: group, settings }) => {
        const drawnSettings = settings.flatMap(drawnEntry)
        const choice = group === 'value' && sources.length > 1
        const validationBoxes = group === 'validation' && (atField.length > 0 || canAdd)
        if (drawnSettings.length === 0 && !choice && !validationBoxes) {
          return null
        }
        const addValidation =
          group === 'validation' && canAdd && list !== undefined ? (
            // Proposed, #4902: a region's main action keeps its border wherever it sits, a title row included.
            <AxisActionRow outlined>
              <AxisButton
                role="secondary"
                onClick={() =>
                  onChange('/validations', [
                    ...list,
                    {
                      name: uniqueStepName(list, 'validation'),
                      expression: '',
                      message: { textKey: '' },
                      field: name,
                    },
                  ])
                }
              >
                {t('shell.studio.entity.addValidation')}
              </AxisButton>
            </AxisActionRow>
          ) : undefined
        return (
          <SectionPart key={group} testName={`field-${group}`}>
            <AxisFieldGroup
              heading
              {...groupTexts(t, group === 'type' ? `fields.group.type.${type}` : `fields.group.${group}`)}
              action={addValidation}
              fold={
                group === 'searchPrivacy'
                  ? { open: privacyOpen, onToggle: () => setPrivacyOpen((open) => !open), state: privacyState }
                  : undefined
              }
            >
              <Flex vertical gap="small" style={{ minWidth: 240 }}>
                {choice && (
                  <AxisChoiceField
                    {...fieldTexts(t, 'fields.valueSource')}
                    // The group's title, Value, names the choice to the eye.
                    hideLabel
                    value={valueSourceOf(field)}
                    // Proposed, #3264: the chosen way's settings sit under it, so they read as its own.
                    options={sources.map((source) => ({
                      value: source,
                      ...fieldTexts(t, `fields.valueSource.${source}`),
                      detail:
                        source === valueSourceOf(field) && drawnSettings.length > 0 ? (
                          <Flex vertical gap="small">
                            {drawnSettings}
                          </Flex>
                        ) : undefined,
                    }))}
                    onChange={(next) => onChange(pointer, withValueSource(field, next as ValueSource))}
                  />
                )}
                {choice ? null : drawnSettings}
                {group === 'validation' && (
                  <>
                    {atField.map(({ validation, position }) => (
                      <ValidationRow
                        key={position}
                        atField
                        index={position}
                        validation={validation}
                        fieldNames={noFieldNames}
                        readOnly={readOnly}
                        layer={layer}
                        onChange={onChange}
                      />
                    ))}
                  </>
                )}
              </Flex>
            </AxisFieldGroup>
          </SectionPart>
        )
      })}
    </SectionParts>
  )
}

/**
 * The Find by and From of a matched reference, see docs/reference/entity-designer.md#settings. Find by offers only the
 * target entity's fields that are `unique`, from the application's names. From offers only this entity's fields of the
 * Find by field's type, and of its target for a reference, without the field itself, as `matchSources` lists them. Both
 * are plain boxes while the names have not loaded, and Find by names no field that the names know, so From offers none.
 * Clearing either leaves it empty and Validate reports it.
 */
function MatchedSource({
  pointer,
  index,
  field,
  matchSources,
  onChange,
}: {
  pointer: string
  index: number
  field: Content
  matchSources: (MatchSource | null)[] | undefined
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const match = isContent(field.match) ? field.match : {}
  const same = (left: string, right: string) => left.toLowerCase() === right.toLowerCase()
  const targetFields = names?.entities.find((entity) => same(entity.name, text(field.target)))?.fields ?? []
  const findBy = targetFields.find((other) => other.unique && same(other.name, text(match.field)))
  return (
    <div data-testid={`studio-field-${pointer}/match`}>
      <Flex vertical gap="small">
        <Picker
          pointer={`${pointer}/match/field`}
          {...fieldTexts(t, 'fields.match.field')}
          options={
            names === undefined
              ? undefined
              : targetFields.flatMap(({ name, labelKey, unique }) => (unique ? [{ name, labelKey }] : []))
          }
          value={match.field}
          required
          onPick={(next) => onChange(`${pointer}/match/field`, next ?? '')}
        />
        <Picker
          pointer={`${pointer}/match/from`}
          {...fieldTexts(t, 'fields.match.from')}
          options={
            names === undefined
              ? undefined
              : findBy === undefined
                ? []
                : (matchSources ?? []).flatMap((source, position) =>
                    source !== null &&
                    position !== index &&
                    source.type === findBy.type &&
                    (findBy.type !== 'reference' || same(source.target, findBy.target ?? ''))
                      ? [source.name]
                      : [],
                  )
          }
          value={match.from}
          required
          onPick={(next) => onChange(`${pointer}/match/from`, next ?? '')}
        />
      </Flex>
    </div>
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
        {...datePickerChevrons}
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
        {...datePickerChevrons}
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
    <SectionPart
      testName="indexes"
      heading={sectionHeading(t, 'indexes')}
      action={
        readOnly ? undefined : (
          <AxisActionRow outlined>
            <AxisButton
              role="secondary"
              onClick={() => write([...list, { name: uniqueStepName(list, 'index'), fields: [] }])}
            >
              {t('shell.studio.entity.addIndex')}
            </AxisButton>
          </AxisActionRow>
        )
      }
    >
      <div data-testid="studio-field-/indexes">
        <Flex vertical gap="large">
          {list.map((index, position) => {
            const pointer = `/indexes/${position}`
            const content = isContent(index) ? index : {}
            return (
              <Flex key={position} vertical gap="small" data-testid={`studio-field-${pointer}`}>
                <Flex gap="small" align="center" justify="space-between">
                  <Flex gap="small" align="center">
                    <Typography.Text code data-testid={`studio-field-${pointer}/name`}>
                      {text(content.name)}
                    </Typography.Text>
                    {layer && <LayerMarkTag layer={layer} list="/indexes" index={position} name={text(content.name)} />}
                  </Flex>
                  {readOnly ? null : (
                    <RowActions
                      fold
                      actions={[
                        {
                          key: 'moveUp',
                          labelKey: 'shell.studio.entity.moveUp',
                          disabled: position === 0,
                          onClick: () => move(position, -1),
                        },
                        {
                          key: 'moveDown',
                          labelKey: 'shell.studio.entity.moveDown',
                          disabled: position === list.length - 1,
                          onClick: () => move(position, 1),
                        },
                        {
                          key: 'remove',
                          labelKey: 'shell.studio.entity.removeIndex',
                          destructive: true,
                          onClick: () => write(list.filter((_, other) => other !== position)),
                        },
                      ]}
                    />
                  )}
                </Flex>
                <div data-testid={`studio-field-${pointer}/fields`}>
                  <AxisField {...fieldTexts(t, 'indexes.fields')} required>
                    <Select
                      mode="multiple"
                      virtual={false}
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
              </Flex>
            )
          })}
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
  const list = validations === undefined ? [] : Array.isArray(validations) ? validations : undefined
  // What a move reads when it is pressed, so each row keeps the same handler between draws, as the Fields table does.
  const latest = useRef({ list, onChange })
  useLayoutEffect(() => {
    latest.current = { list, onChange }
  })
  const move = useCallback((index: number, offset: number) => {
    const { list: current, onChange: change } = latest.current
    const next = [...(current ?? [])]
    ;[next[index], next[index + offset]] = [next[index + offset], next[index]]
    change('/validations', next)
  }, [])

  if (list === undefined) {
    return (
      <SectionPart testName="validations" heading={sectionHeading(t, 'validations')}>
        <div data-testid="studio-field-/validations">
          <JsonOnly />
        </div>
      </SectionPart>
    )
  }

  return (
    <SectionPart
      testName="validations"
      heading={sectionHeading(t, 'validations')}
      action={
        readOnly ? undefined : (
          <AxisActionRow outlined>
            <AxisButton
              role="secondary"
              onClick={() =>
                onChange('/validations', [
                  ...list,
                  { name: uniqueStepName(list, 'validation'), expression: '', message: { textKey: '' }, field: '' },
                ])
              }
            >
              {t('shell.studio.entity.addValidation')}
            </AxisButton>
          </AxisActionRow>
        )
      }
    >
      <div data-testid="studio-field-/validations">
        <Flex vertical gap="large">
          {list.map((validation, index) => (
            <ValidationRow
              key={index}
              index={index}
              validation={validation}
              fieldNames={fieldNames}
              readOnly={readOnly}
              layer={layer}
              onChange={onChange}
              onMove={move}
              last={index === list.length - 1}
            />
          ))}
          <RemovedRows layer={layer} list="/validations" />
        </Flex>
      </div>
    </SectionPart>
  )
})

/**
 * One validation of the entity, which draws again only when its own validation or the field names change. With
 * `atField`, as in the field drawer's Validation group, it has no field picker and its message is typed as label texts,
 * see docs/reference/entity-designer.md#settings. Its "…" menu holds Remove validation, and Move up and Move down too
 * when the section gives `onMove`, as the drawer does not, see docs/reference/entity-designer.md#validations. `last` is
 * the validation at the end of the section, where Move down is off.
 */
const ValidationRow = memo(function ValidationRow({
  index,
  atField = false,
  validation,
  fieldNames,
  readOnly,
  layer,
  onChange,
  onMove,
  last = false,
}: {
  index: number
  atField?: boolean
  validation: unknown
  fieldNames: string[]
  readOnly: boolean
  layer: DesignerLayer | undefined
  onChange: ChangeHandler
  onMove?: (index: number, offset: number) => void
  last?: boolean
}) {
  const t = useText()
  const pointer = `/validations/${index}`
  const content = isContent(validation) ? validation : {}
  const expression = fieldTexts(t, 'validations.expression')
  return (
    <Flex vertical gap="small" data-testid={`studio-field-${pointer}`}>
      <Flex gap="small" align="center" justify="space-between">
        <Flex gap="small" align="center">
          <Typography.Text code data-testid={`studio-field-${pointer}/name`}>
            {text(content.name)}
          </Typography.Text>
          {layer && <LayerMarkTag layer={layer} list="/validations" index={index} name={text(content.name)} />}
        </Flex>
        {/* Proposed, #4902: a "…" menu never holds one entry, so a validation with Remove alone, in a field's panel, shows
            it as an icon button, as a table row's delete is. */}
        {readOnly ? null : (
          <RowActions
            fold={onMove !== undefined}
            actions={[
              ...(onMove === undefined
                ? []
                : [
                    {
                      key: 'moveUp',
                      labelKey: 'shell.studio.entity.moveUp',
                      disabled: index === 0,
                      onClick: () => onMove(index, -1),
                    },
                    {
                      key: 'moveDown',
                      labelKey: 'shell.studio.entity.moveDown',
                      disabled: last,
                      onClick: () => onMove(index, 1),
                    },
                  ]),
              {
                key: 'remove',
                labelKey: 'shell.studio.entity.removeValidation',
                icon: <DeleteOutlined />,
                destructive: true,
                onClick: () => onChange(pointer, undefined),
              },
            ]}
          />
        )}
      </Flex>
      <div data-testid={`studio-field-${pointer}/expression`}>
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
      {atField ? (
        <div data-testid={`studio-field-${pointer}/message`}>
          <AxisFieldGroup {...groupTexts(t, 'validations.messageText')}>
            <LabelTexts pointer={`${pointer}/message`} value={content.message} onChange={onChange} />
          </AxisFieldGroup>
        </div>
      ) : (
        <>
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
        </>
      )}
    </Flex>
  )
})
