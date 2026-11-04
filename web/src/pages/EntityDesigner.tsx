import { ArrowDownOutlined, ArrowUpOutlined, CheckOutlined, DeleteOutlined, WarningOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Checkbox,
  ConfigProvider,
  Drawer,
  Flex,
  Input,
  InputNumber,
  Radio,
  Select,
  Switch,
  Tag,
  Tooltip,
  Typography,
  DatePicker,
} from 'antd'
import dayjs from 'dayjs'
import { lazy, Suspense, useContext, useState, type HTMLAttributes } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisCheckboxField, AxisField } from '../platform/AxisField'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import { searchRows } from '../platform/tableControls'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import {
  changeFieldType,
  editableSettings,
  fieldSettings,
  fieldTypes,
  type FieldSetting,
} from '../platform/entityFields'
import type { FieldOrigin, FieldWarning } from '../platform/entityMigrations'
import { escapePointerSegment } from '../platform/jsonPatch'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { SectionPanel } from './FormSection'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { uniqueStepName } from './processPalette'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'
import { setOwn } from '../platform/ownProperty'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

type Content = Record<string, unknown>

/** The settings that the settings column shows, in the order it shows them. `required` and `unique` have columns. */
const settingColumns: FieldSetting[] = [
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

/** The editor of an expression: the expression editor when the draft can check expressions, a plain box otherwise. */
function Expression({
  pointer,
  name,
  value,
  required,
  readOnly,
  onChange,
}: {
  pointer: string
  name: string
  value: string
  required: boolean
  readOnly: boolean
  onChange: ChangeHandler
}) {
  const check = useContext(ExpressionCheckContext)

  return check && !readOnly ? (
    <Suspense fallback={<Input aria-label={name} value={value} readOnly />}>
      <LazyExpressionEditor
        application={check.application}
        resourceId={check.resourceId}
        path={pointer}
        name={name}
        value={value}
        required={required}
        options={expressionScope(check.kind, check.draft, pointer, check.names)}
        onChange={onChange}
      />
    </Suspense>
  ) : (
    <Input
      aria-label={name}
      value={value}
      onChange={(event) => onChange(pointer, event.target.value === '' && !required ? undefined : event.target.value)}
    />
  )
}

/**
 * A picker over `options`, or over the application's names of `kind` when `options` is not given. It is a plain box
 * while the application's names have not loaded and `options` is not given.
 */
function Picker({
  pointer,
  label,
  kind,
  options,
  value,
  required,
  onPick,
}: {
  pointer: string
  label: string
  kind?: string
  options?: string[]
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
      <div>{label}</div>
      {choices === undefined ? (
        <Input
          aria-label={label}
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
    </div>
  )
}

/** Props every part of the entity designer shares. */
interface EntityParts {
  value: Content
  onChange: ChangeHandler
  readOnly?: boolean
  origins?: FieldOrigin[]
  warnings?: Map<number, FieldWarning>
  onOriginsChange?: (next: FieldOrigin[], touched?: string) => void
}

/**
 * The designer of an entity, on the editor's Form tab, with the JSON tab beside it.
 *
 * Proposed: it is made of the sections the application's Settings has, `SectionPanel` and `AxisField`, so every form of
 * Studio reads the same way: General, then Fields, Validations and Indexes. General has the entity's name, label,
 * description and display field, one to a row, each with its help text. Fields is the standard table, one row a field,
 * with Add field in its heading, and a field's settings open in a drawer beside it, so the list stays in sight. Every
 * input keeps its `studio-field-<pointer>` id and each row the id of its field, so a diagnostic opens at the row.
 */
export function EntityDesigner(props: EntityParts) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const { value, onChange, readOnly = false } = props
  const fields = Array.isArray(value.fields) ? value.fields : undefined
  const fieldNames = (fields ?? []).flatMap((field) =>
    isContent(field) && typeof field.name === 'string' && field.name !== '' ? [field.name] : [],
  )
  const section = (key: string) => ({
    title: t(`shell.studio.section.entity.${key}`),
    description: t(`shell.studio.section.entity.${key}.description`),
    testId: key,
  })
  // The field whose drawer is open, by its place in the fields.
  const [openField, setOpenField] = useState<number | null>(null)

  function addField() {
    onChange('/fields', [...fields!, { name: '', type: 'text' }])
    props.onOriginsChange?.([...fields!.map((_, index) => props.origins?.[index] ?? null), null])
    setOpenField(fields!.length)
  }

  const designer = (
    <Flex vertical gap={spacing.md} data-testid="studio-entity-designer">
      <SectionPanel {...section('general')}>
        <EntityGeneral value={value} onChange={onChange} readOnly={readOnly} fieldNames={fieldNames} />
      </SectionPanel>
      <SectionPanel
        {...section('fields')}
        action={
          readOnly || fields === undefined ? undefined : (
            <AxisButton role="main" onClick={addField}>
              {t('shell.studio.entity.addField')}
            </AxisButton>
          )
        }
      >
        <EntityFields
          fields={fields}
          readOnly={readOnly}
          origins={props.origins}
          warnings={props.warnings}
          onOriginsChange={props.onOriginsChange}
          onChange={onChange}
          openIndex={openField}
          onOpen={setOpenField}
        />
      </SectionPanel>
      <SectionPanel {...section('validations')}>
        <Validations validations={value.validations} fieldNames={fieldNames} readOnly={readOnly} onChange={onChange} />
      </SectionPanel>
      <SectionPanel {...section('indexes')}>
        <Indexes indexes={value.indexes} fields={fields ?? []} readOnly={readOnly} onChange={onChange} />
      </SectionPanel>
    </Flex>
  )
  return readOnly ? <ConfigProvider componentDisabled>{designer}</ConfigProvider> : designer
}

/** The text of a label or description in the interface's language, else in the folder's first, else its text key. */
function useLabelText(): (value: unknown) => string | null {
  const names = useContext(StudioNamesContext)
  const resource = useContext(LabelTextsContext)
  const uiLocale = useContext(PlatformContext)?.current.locale ?? 'en'
  return (value) => {
    const key = isContent(value) && typeof value.textKey === 'string' ? value.textKey : null
    if (key === null || key === '') {
      return null
    }
    const texts = resource?.pending?.[key] ?? names?.texts.find((entry) => entry.key === key)?.texts
    if (!texts) {
      return key
    }
    const locale = names?.locales.includes(uiLocale) ? uiLocale : names?.locales[0]
    return (locale && texts[locale]?.trim()) || key
  }
}

/** The help text of the field text `key`, which every field has: a test checks the catalogue holds each one. */
function helpOf(t: (key: string) => string, key: string): string {
  return t(`${key}.help`)
}

/**
 * Proposed: the General section: the entity's name, label, description and display field, one to a row, each with
 * its help text. A module's entity shows no description, as before.
 */
function EntityGeneral({
  value,
  onChange,
  readOnly,
  fieldNames,
}: {
  value: Content
  onChange: ChangeHandler
  readOnly: boolean
  fieldNames: string[]
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  const texts = names !== undefined && label !== null && !readOnly
  const field = (name: string) => ({
    testId: `studio-field-/${name}`,
    label: t(`shell.studio.field.entity.${name}`),
    help: helpOf(t, `shell.studio.field.entity.${name}`),
  })
  const textKeyInput = (name: string, pointer: string, current: unknown) => (
    <div data-testid={`studio-field-${pointer}/textKey`}>
      <Input
        aria-label={name}
        value={text(isContent(current) ? current.textKey : undefined)}
        onChange={(event) => onChange(`${pointer}/textKey`, event.target.value)}
      />
    </div>
  )
  return (
    <>
      <AxisField {...field('name')} required>
        <Input
          aria-label={t('shell.studio.field.entity.name')}
          aria-required
          value={text(value.name)}
          onChange={(event) => onChange('/name', event.target.value)}
        />
      </AxisField>
      <AxisField {...field('label')}>
        {texts ? (
          <LabelTexts pointer="/label" value={value.label} onChange={onChange} />
        ) : (
          textKeyInput(t('shell.studio.labelKey'), '/label', value.label)
        )}
      </AxisField>
      {!readOnly && (
        <AxisField {...field('description')}>
          {texts ? (
            <LabelTexts pointer="/description" value={value.description} onChange={onChange} />
          ) : (
            textKeyInput(t('shell.studio.field.entity.description'), '/description', value.description)
          )}
        </AxisField>
      )}
      <AxisField {...field('displayField')}>
        <ReferencePicker
          name={t('shell.studio.field.entity.displayField')}
          value={typeof value.displayField === 'string' ? value.displayField : undefined}
          required={false}
          options={fieldNames}
          block
          onChange={(next) => onChange('/displayField', next)}
        />
      </AxisField>
    </>
  )
}

/** Proposed: what a field's type says in the Type column: its name, then the entity it points to or its number of values. */
function TypeCell({ field }: { field: Content }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const type = text(field.type)
  if (type === '') {
    return <EmptyDash />
  }
  const target = text(field.target)
  const values = Array.isArray(field.values) ? field.values.length : 0
  const detail =
    target !== ''
      ? `→ ${target}`
      : type === 'enum'
        ? t('shell.studio.entity.valueCount').replace('{count}', String(values))
        : null
  return (
    <span>
      {t(`shell.studio.entity.type.${type}`)}
      {detail && <span style={{ color: colour.textMuted }}> {detail}</span>}
    </span>
  )
}

/** Proposed: the settings that set a field apart, as neutral tags in the Properties column. */
function fieldTraits(field: Content): string[] {
  return [
    typeof field.expression === 'string' && field.expression !== '' ? 'shell.studio.entity.computed' : null,
    typeof field.sequence === 'string' && field.sequence !== '' ? 'shell.studio.entity.sequence' : null,
    field.creator === true ? 'shell.studio.entity.creator' : null,
    field.default !== undefined ? 'shell.studio.entity.default' : null,
    field.searchable === true ? 'shell.studio.entity.searchable' : null,
    field.sensitive === true ? 'shell.studio.entity.sensitive' : null,
  ].filter((key): key is string => key !== null)
}

/** A yes cell of Required or Unique: a check, named for screen readers. A no is the dash. */
function Yes({ name }: { name: string }) {
  const { colour } = useDesignTokens()
  return <CheckOutlined aria-label={name} style={{ color: colour.primaryText }} />
}

/**
 * Proposed: the entity's fields as the standard table, in the order of the file, which is the order a default form
 * shows them in, so the table has no sort by column. Its columns are Name, a link that opens the field's drawer,
 * Label, Type, Required, Unique and Properties, the settings that set the field apart. Search looks in the name and
 * label. A row's actions are Move up, Move down and Remove; moving is off while a search narrows the rows. Add field
 * adds a text field and opens its drawer.
 */
function EntityFields({
  fields,
  readOnly,
  origins,
  warnings,
  onOriginsChange,
  onChange,
  openIndex,
  onOpen,
}: {
  fields: unknown[] | undefined
  readOnly: boolean
  origins: FieldOrigin[] | undefined
  warnings: Map<number, FieldWarning> | undefined
  onOriginsChange: ((next: FieldOrigin[], touched?: string) => void) | undefined
  onChange: ChangeHandler
  /** The field whose drawer is open, or none. */
  openIndex: number | null
  onOpen: (index: number | null) => void
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const labelText = useLabelText()
  const [search, setSearch] = useState('')

  if (fields === undefined) {
    return (
      <div data-testid="studio-field-/fields">
        <JsonOnly />
      </div>
    )
  }

  /** The origin of each row, `null` for a row that has none. */
  const rowOrigins = (): FieldOrigin[] => fields.map((_, index) => origins?.[index] ?? null)

  function move(index: number, offset: number) {
    const next = [...fields!]
    ;[next[index], next[index + offset]] = [next[index + offset], next[index]]
    onChange('/fields', next)
    if (onOriginsChange) {
      const moved = rowOrigins()
      ;[moved[index], moved[index + offset]] = [moved[index + offset], moved[index]]
      onOriginsChange(moved)
    }
  }

  function removeField(index: number) {
    onOpen(null)
    onChange(
      '/fields',
      fields!.filter((_, position) => position !== index),
    )
    onOriginsChange?.(
      rowOrigins().filter((_, position) => position !== index),
      origins?.[index] ?? undefined,
    )
  }

  type Row = { index: number; field: Content }
  const rows: Row[] = fields.map((field, index) => ({ index, field: isContent(field) ? field : {} }))
  const columns: AxisColumn<Row>[] = [
    {
      key: 'name',
      kind: 'fit',
      title: t('shell.studio.entity.fieldName'),
      searchText: (row) => text(row.field.name),
      render: (row) => (
        <Flex gap={spacing.xs} align="center">
          {/* A button, not a link, as a field has no address of its own; it takes focus, so the drawer gives it back. */}
          <Button
            type="link"
            data-testid={`studio-entity-field-open-${row.index}`}
            style={{ color: colour.primaryText, padding: 0, height: 'auto' }}
            onClick={() => onOpen(row.index)}
          >
            {text(row.field.name) || t('shell.studio.entity.unnamedField')}
          </Button>
          {warnings?.get(row.index) && (
            <Tooltip title={t('shell.studio.entity.migration.rowWarning')}>
              <WarningOutlined
                aria-label={t('shell.studio.entity.migration.rowWarning')}
                style={{ color: colour.warningText }}
              />
            </Tooltip>
          )}
        </Flex>
      ),
    },
    {
      key: 'label',
      kind: 'text',
      title: t('shell.studio.entity.fieldLabel'),
      searchText: (row) => labelText(row.field.label) ?? '',
      render: (row) => labelText(row.field.label) ?? <EmptyDash />,
    },
    {
      key: 'type',
      kind: 'fit',
      title: t('shell.studio.entity.fieldType'),
      render: (row) => <TypeCell field={row.field} />,
    },
    {
      key: 'required',
      kind: 'fit',
      title: t('shell.studio.entity.required'),
      render: (row) => (row.field.required === true ? <Yes name={t('shell.studio.entity.required')} /> : <EmptyDash />),
    },
    {
      key: 'unique',
      kind: 'fit',
      title: t('shell.studio.entity.unique'),
      render: (row) => (row.field.unique === true ? <Yes name={t('shell.studio.entity.unique')} /> : <EmptyDash />),
    },
    {
      key: 'properties',
      kind: 'text',
      priority: 'low',
      title: t('shell.studio.entity.properties'),
      render: (row) => {
        const traits = fieldTraits(row.field)
        return traits.length === 0 ? (
          <EmptyDash />
        ) : (
          <Flex gap={spacing.xs} wrap>
            {traits.map((key) => (
              <Tag key={key} style={{ marginInlineEnd: 0 }}>
                {t(key)}
              </Tag>
            ))}
          </Flex>
        )
      },
    },
  ]
  const searching = search.trim() !== ''
  const shown = searchRows(rows, columns, search)
  const open = openIndex !== null && openIndex < fields.length ? openIndex : null

  return (
    <div data-testid="studio-field-/fields">
      <AxisTable<Row>
        testId="studio-entity-fields"
        rows={shown}
        rowKey={(row) => row.index}
        columns={columns}
        onRow={(row) => ({ 'data-testid': `studio-field-/fields/${row.index}` }) as HTMLAttributes<HTMLElement>}
        rowActions={
          readOnly
            ? undefined
            : {
                count: 3,
                of: (row) => [
                  {
                    key: 'up',
                    labelKey: 'shell.studio.entity.moveUp',
                    icon: <ArrowUpOutlined />,
                    disabled: searching || row.index === 0,
                    onClick: () => move(row.index, -1),
                  },
                  {
                    key: 'down',
                    labelKey: 'shell.studio.entity.moveDown',
                    icon: <ArrowDownOutlined />,
                    disabled: searching || row.index === fields.length - 1,
                    onClick: () => move(row.index, 1),
                  },
                  {
                    key: 'remove',
                    labelKey: 'shell.studio.entity.removeField',
                    icon: <DeleteOutlined />,
                    destructive: true,
                    onClick: () => removeField(row.index),
                  },
                ],
              }
        }
        toolbar={{
          view: (
            <Input.Search
              aria-label={t('shell.studio.search')}
              placeholder={t('shell.studio.search')}
              allowClear
              style={{ maxWidth: 360 }}
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
          ),
        }}
        emptyText={t('shell.studio.entity.noFields')}
        pagination={false}
      />
      <FieldDrawer
        index={open}
        fields={fields}
        readOnly={readOnly}
        warning={open === null ? undefined : warnings?.get(open)}
        onChange={onChange}
        onClose={() => onOpen(null)}
      />
    </div>
  )
}

/**
 * Proposed: one field's settings, in a drawer beside the fields table, so the table stays in sight: its name, label,
 * type, Required and Unique, help, then the settings its type takes. Each edit goes into the draft at once.
 */
function FieldDrawer({
  index,
  fields,
  readOnly,
  warning,
  onChange,
  onClose,
}: {
  index: number | null
  fields: unknown[]
  readOnly: boolean
  warning: FieldWarning | undefined
  onChange: ChangeHandler
  onClose: () => void
}) {
  const t = useText()
  const labelText = useLabelText()
  const field = index === null ? {} : isContent(fields[index]) ? (fields[index] as Content) : {}
  const pointer = `/fields/${index}`
  // Proposed: a field's label and help are typed as texts, one box per locale, as the entity's label is, when the
  // folder's names have loaded; a module's entity shows their text keys.
  const names = useContext(StudioNamesContext)
  const resource = useContext(LabelTextsContext)
  const typesTexts = names !== undefined && resource !== null && !readOnly
  // Proposed: a field's settings have the texts `shell.studio.field.entity.fields.<setting>` and their help, as the schema form names a field.
  const setting = (name: string, marked = true) => ({
    testId: marked ? `studio-field-${pointer}/${name}` : undefined,
    label: t(`shell.studio.field.entity.fields.${name}`),
    help: helpOf(t, `shell.studio.field.entity.fields.${name}`),
  })
  return (
    <Drawer
      open={index !== null}
      size={560}
      title={labelText(field.label) ?? (text(field.name) || t('shell.studio.entity.unnamedField'))}
      onClose={onClose}
      data-testid="studio-entity-field-drawer"
    >
      {index !== null && (
        <Flex vertical gap="middle">
          <AxisField {...setting('name')} required>
            <Input
              aria-label={t('shell.studio.field.entity.fields.name')}
              aria-required
              value={text(field.name)}
              onChange={(event) => onChange(`${pointer}/name`, event.target.value)}
            />
          </AxisField>
          <AxisField {...setting('label', typesTexts)}>
            {typesTexts ? (
              <LabelTexts pointer={`${pointer}/label`} value={field.label} onChange={onChange} />
            ) : (
              <FieldLabel
                name={t('shell.studio.field.entity.fields.label')}
                pointer={`${pointer}/label`}
                value={field.label}
                onChange={onChange}
              />
            )}
          </AxisField>
          <AxisField {...setting('type')} required>
            <Select
              aria-label={t('shell.studio.field.entity.fields.type')}
              aria-required
              showSearch
              optionFilterProp="label"
              virtual={false}
              style={{ width: '100%' }}
              value={typeof field.type === 'string' ? field.type : undefined}
              options={fieldTypes.map((option) => ({ value: option, label: t(`shell.studio.entity.type.${option}`) }))}
              onChange={(next: string) => onChange(pointer, changeFieldType(field, next))}
            />
          </AxisField>
          <FlagRow field={field} setting="required" pointer={`${pointer}/required`} onChange={onChange} />
          <FlagRow field={field} setting="unique" pointer={`${pointer}/unique`} onChange={onChange} />
          <AxisField {...setting('help', typesTexts)}>
            {typesTexts ? (
              <LabelTexts pointer={`${pointer}/help`} value={field.help} onChange={onChange} />
            ) : (
              <FieldLabel
                name={t('shell.studio.field.entity.fields.help')}
                pointer={`${pointer}/help`}
                value={field.help}
                onChange={onChange}
              />
            )}
          </AxisField>
          <Settings index={index} field={field} fields={fields} readOnly={readOnly} onChange={onChange} />
          {warning && (
            <Alert
              type="warning"
              data-testid={`studio-migration-warning-${pointer}`}
              title={t(
                warning === 'enum'
                  ? 'shell.studio.entity.migration.enumWarning'
                  : 'shell.studio.entity.migration.uniqueWarning',
              )}
            />
          )}
        </Flex>
      )}
    </Drawer>
  )
}

/** Required or Unique, as a yes-or-no field with its help. It shows only when the field's type takes the setting. */
function FlagRow({
  field,
  setting,
  pointer,
  onChange,
}: {
  field: Content
  setting: FieldSetting
  pointer: string
  onChange: ChangeHandler
}) {
  const t = useText()
  if (!fieldSettings(text(field.type)).includes(setting)) {
    return null
  }
  const key = `shell.studio.field.entity.fields.${setting}`
  return (
    <AxisCheckboxField
      label={t(key)}
      help={helpOf(t, key)}
      checked={field[setting] === true}
      testId={`studio-field-${pointer}`}
      onChange={(checked) => onChange(pointer, checked)}
    />
  )
}

/** The text key picker of a field's label or help, named `name`. Clearing it removes the text. */
function FieldLabel({
  name,
  pointer,
  value,
  onChange,
}: {
  name: string
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
}

/** The settings of one field that its type takes, each with its own input and id. */
function Settings({
  index,
  field,
  fields,
  readOnly,
  onChange,
}: {
  index: number
  field: Content
  fields: unknown[]
  readOnly: boolean
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/fields/${index}`
  const taken = editableSettings(field)
  const shown = settingColumns.filter((setting) => taken.includes(setting))

  const labelOf = (setting: FieldSetting) => t(`shell.studio.entity.${setting}`)

  function number(setting: 'maxLength' | 'minLength' | 'precision' | 'scale' | 'maxSize') {
    return (
      <InputNumber
        aria-label={labelOf(setting)}
        precision={0}
        min={setting === 'maxSize' || setting === 'minLength' ? 1 : 0}
        value={typeof field[setting] === 'number' ? field[setting] : null}
        onChange={(next) => onChange(`${pointer}/${setting}`, typeof next === 'number' ? next : undefined)}
      />
    )
  }

  function flag(setting: 'creator' | 'sensitive' | 'exactLookup') {
    return (
      <Checkbox
        aria-label={labelOf(setting)}
        checked={field[setting] === true}
        // Even a false exactLookup is refused on a field that is not sensitive, so unchecking removes it.
        onChange={(event) =>
          onChange(
            `${pointer}/${setting}`,
            setting === 'exactLookup' && !event.target.checked ? undefined : event.target.checked,
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
      case 'creator':
      case 'sensitive':
      case 'exactLookup':
        return flag(setting)
      case 'searchable':
        return (
          <Switch
            aria-label={labelOf(setting)}
            checked={field.searchable === true}
            onChange={(checked) => onChange(`${pointer}/searchable`, checked ? true : undefined)}
          />
        )
      case 'min':
      case 'max':
        return (
          <Limit
            type={text(field.type)}
            value={field[setting]}
            label={labelOf(setting)}
            onChange={(next) => onChange(`${pointer}/${setting}`, next)}
          />
        )
      case 'default':
        return (
          <Expression
            pointer={`${pointer}/default`}
            name={labelOf(setting)}
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
            aria-label={labelOf(setting)}
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
            aria-label={labelOf(setting)}
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
            name={labelOf(setting)}
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

  return (
    <Flex vertical gap="small" style={{ minWidth: 240 }}>
      {shown.map((setting) =>
        setting === 'currency' ? (
          <CurrencySource
            key={setting}
            pointer={pointer}
            field={field}
            currencyFields={fields.flatMap((other, position) =>
              position !== index &&
              isContent(other) &&
              (other.type === 'enum' || other.type === 'text') &&
              typeof other.name === 'string' &&
              other.name !== ''
                ? [other.name]
                : [],
            )}
            onChange={onChange}
          />
        ) : setting === 'target' || setting === 'sequence' ? (
          <Picker
            key={setting}
            pointer={`${pointer}/${setting}`}
            label={labelOf(setting)}
            kind={setting === 'target' ? 'entity' : 'sequence'}
            value={field[setting]}
            required={setting === 'target'}
            onPick={(next) => onChange(`${pointer}/${setting}`, next ?? (setting === 'target' ? '' : undefined))}
          />
        ) : (
          <div key={setting} data-testid={`studio-field-${pointer}/${setting}`}>
            <div>{labelOf(setting)}</div>
            {input(setting)}
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
  label,
  onChange,
}: {
  type: string
  value: unknown
  label: string
  onChange: (next: number | string | undefined) => void
}) {
  if (type === 'date') {
    return (
      <DatePicker
        aria-label={label}
        value={typeof value === 'string' && dayjs(value).isValid() ? dayjs(value) : null}
        onChange={(date) => onChange(date ? date.format('YYYY-MM-DD') : undefined)}
      />
    )
  }
  if (type === 'date-time') {
    return (
      <DatePicker
        aria-label={label}
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
      aria-label={label}
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
      <div>
        <div>{t('shell.studio.entity.currencySource')}</div>
        <Radio.Group
          role="radiogroup"
          aria-label={t('shell.studio.entity.currencySource')}
          value={fromField ? 'field' : 'code'}
          onChange={(event) => switchTo(event.target.value)}
        >
          <Radio value="code">{t('shell.studio.entity.currencyFixed')}</Radio>
          <Radio value="field">{t('shell.studio.entity.currencyFromField')}</Radio>
        </Radio.Group>
      </div>
      {fromField ? (
        <Picker
          pointer={`${pointer}/currencyField`}
          label={t('shell.studio.entity.currencyField')}
          options={currencyFields}
          value={field.currencyField}
          required
          onPick={(next) => onChange(`${pointer}/currencyField`, next ?? '')}
        />
      ) : (
        <div data-testid={`studio-field-${pointer}/currency`}>
          <div>{t('shell.studio.entity.currency')}</div>
          <Input
            aria-label={t('shell.studio.entity.currency')}
            value={text(field.currency)}
            onChange={(event) =>
              onChange(`${pointer}/currency`, event.target.value === '' ? undefined : event.target.value.toUpperCase())
            }
          />
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
    <Flex vertical gap="small">
      {values.map((value) => {
        const name = `${t('shell.studio.entity.valueLabel')} ${value}`
        const current = labels[value]
        const key = isContent(current) && typeof current.textKey === 'string' ? current.textKey : undefined
        return (
          <div key={value} data-testid={`studio-field-${pointer}/${escapePointerSegment(value)}`}>
            <div>{name}</div>
            {names === undefined ? (
              <Input
                aria-label={name}
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
          </div>
        )
      })}
    </Flex>
  )
}

/**
 * The indexes of the entity as rows of a name, a field picker and a Unique choice. The picker offers the fields that have a
 * column and writes the picked names in order. Every change writes the whole `indexes` array, and an empty list
 * removes the key.
 */
function Indexes({
  indexes,
  fields,
  readOnly,
  onChange,
}: {
  indexes: unknown
  fields: unknown[]
  readOnly: boolean
  onChange: ChangeHandler
}) {
  const t = useText()

  if (indexes !== undefined && !Array.isArray(indexes)) {
    return (
      <div data-testid="studio-field-/indexes">
        <JsonOnly />
      </div>
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
    <div data-testid="studio-field-/indexes">
      <Flex vertical gap="small">
        {list.map((index, position) => {
          const pointer = `/indexes/${position}`
          const content = isContent(index) ? index : {}
          return (
            <Flex key={position} gap="small" align="flex-start" wrap data-testid={`studio-field-${pointer}`}>
              <Typography.Text code data-testid={`studio-field-${pointer}/name`}>
                {text(content.name)}
              </Typography.Text>
              <div data-testid={`studio-field-${pointer}/fields`} style={{ flex: 1, minWidth: 240 }}>
                <div>{t('shell.studio.entity.indexFields')}</div>
                <Select
                  mode="multiple"
                  aria-label={t('shell.studio.entity.indexFields')}
                  virtual={false}
                  style={{ width: '100%' }}
                  value={Array.isArray(content.fields) ? content.fields.filter((item) => typeof item === 'string') : []}
                  options={choices.map((name) => ({ value: name, label: name }))}
                  onChange={(next: string[]) => onChange(`${pointer}/fields`, next)}
                />
              </div>
              <div data-testid={`studio-field-${pointer}/unique`}>
                <Checkbox
                  aria-label={t('shell.studio.entity.indexUnique')}
                  checked={content.unique === true}
                  onChange={(event) => onChange(`${pointer}/unique`, event.target.checked ? true : undefined)}
                >
                  {t('shell.studio.entity.indexUnique')}
                </Checkbox>
              </div>
              {readOnly ? null : (
                <Flex gap="small">
                  <Button disabled={position === 0} onClick={() => move(position, -1)}>
                    {t('shell.studio.entity.moveUp')}
                  </Button>
                  <Button disabled={position === list.length - 1} onClick={() => move(position, 1)}>
                    {t('shell.studio.entity.moveDown')}
                  </Button>
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
            <Button onClick={() => write([...list, { name: uniqueStepName(list, 'index'), fields: [] }])}>
              {t('shell.studio.entity.addIndex')}
            </Button>
          </div>
        )}
      </Flex>
    </div>
  )
}

/** The validations of the entity as rows of a name, an expression, a message key and a field. */
function Validations({
  validations,
  fieldNames,
  readOnly,
  onChange,
}: {
  validations: unknown
  fieldNames: string[]
  readOnly: boolean
  onChange: ChangeHandler
}) {
  const t = useText()

  if (validations !== undefined && !Array.isArray(validations)) {
    return (
      <div data-testid="studio-field-/validations">
        <JsonOnly />
      </div>
    )
  }
  const list: unknown[] = validations ?? []

  return (
    <div data-testid="studio-field-/validations">
      <Flex vertical gap="small">
        {list.map((validation, index) => {
          const pointer = `/validations/${index}`
          const content = isContent(validation) ? validation : {}
          const name = t('shell.studio.entity.validationExpression')
          return (
            <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${pointer}`}>
              <Typography.Text code data-testid={`studio-field-${pointer}/name`}>
                {text(content.name)}
              </Typography.Text>
              <div data-testid={`studio-field-${pointer}/expression`} style={{ flex: 1, minWidth: 240 }}>
                <div>{name}</div>
                <Expression
                  pointer={`${pointer}/expression`}
                  name={name}
                  value={text(content.expression)}
                  required
                  readOnly={readOnly}
                  onChange={onChange}
                />
              </div>
              <Picker
                pointer={`${pointer}/message/textKey`}
                label={t('shell.studio.entity.validationMessage')}
                kind="text"
                value={isContent(content.message) ? content.message.textKey : undefined}
                required
                onPick={(next) => onChange(`${pointer}/message/textKey`, next ?? '')}
              />
              <Picker
                pointer={`${pointer}/field`}
                label={t('shell.studio.entity.validationField')}
                options={fieldNames}
                value={content.field}
                required
                onPick={(next) => onChange(`${pointer}/field`, next ?? '')}
              />
              {readOnly ? null : (
                <Button danger onClick={() => onChange(pointer, undefined)}>
                  {t('shell.studio.entity.removeValidation')}
                </Button>
              )}
            </Flex>
          )
        })}
        {readOnly ? null : (
          <div>
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
      </Flex>
    </div>
  )
}
