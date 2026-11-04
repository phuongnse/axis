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
  Tabs,
  Tag,
  Tooltip,
  Typography,
  DatePicker,
  type TabsProps,
} from 'antd'
import dayjs from 'dayjs'
import { lazy, Suspense, useContext, useState, type HTMLAttributes, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
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
import { LabelTexts } from './LabelTexts'
import { ResourceDescription } from './ResourceDescription'
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
 * The designer of an entity, as a module's entity shows it read-only: its header, then its parts as tabs. The editor of
 * a resource of the folder draws the same header and tabs through {@link EntityHeader} and {@link entityTabItems}, with
 * its JSON tab after them.
 *
 * Proposed: the entity designer reads as the long-standing table designers do (Salesforce's Object Manager, Dataverse's
 * table designer, Directus's data model): a header that sums up the entity, with Edit details, then one tab for each
 * part, Fields, Validations and Indexes, each with its count. Fields is the standard table, one row a field, and a field's
 * settings open in a drawer beside it, so the list stays in sight. Every input keeps its `studio-field-<pointer>` id and
 * each row the id of its field, so a diagnostic opens at the row.
 */
export function EntityDesigner(props: EntityParts) {
  const designer = (
    <Flex vertical gap="middle" data-testid="studio-entity-designer">
      <EntityHeader value={props.value} onChange={props.onChange} readOnly={props.readOnly} />
      <Tabs items={entityTabItems(props, (part) => part)} />
    </Flex>
  )
  return props.readOnly ? <ConfigProvider componentDisabled>{designer}</ConfigProvider> : designer
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

/**
 * Proposed: the editor's tabs, where an entity's Fields, Validations and Indexes stand in place of the item `form`. With
 * no `entity`, the tabs are as given.
 */
export function EntityTabs({
  entity,
  wrap,
  items = [],
  ...tabs
}: TabsProps & { entity: EntityParts | null; wrap: (part: ReactNode) => ReactNode }) {
  return (
    <Tabs
      {...tabs}
      items={
        entity === null ? items : items.flatMap((item) => (item.key === 'form' ? entityTabItems(entity, wrap) : [item]))
      }
    />
  )
}

/** A tab's name with the number of its rows after it, in `textMuted`. */
function CountedTab({ nameKey, count }: { nameKey: string; count: number }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  return (
    <span>
      {t(nameKey)}
      <span style={{ color: colour.textMuted, marginInlineStart: spacing.xs }}>{count}</span>
    </span>
  )
}

/**
 * Proposed: the entity's parts as tab items: Fields, under the key `form` so a diagnostic of the form lands on it, then
 * Validations and Indexes. `wrap` puts each part in what the editor puts around its form.
 */
function entityTabItems(
  props: EntityParts,
  wrap: (part: ReactNode) => ReactNode,
): { key: string; label: ReactNode; children: ReactNode }[] {
  const { value, onChange, readOnly = false } = props
  const fields = Array.isArray(value.fields) ? value.fields : undefined
  const fieldNames = (fields ?? []).flatMap((field) =>
    isContent(field) && typeof field.name === 'string' && field.name !== '' ? [field.name] : [],
  )
  const count = (list: unknown) => (Array.isArray(list) ? list.length : 0)
  return [
    {
      key: 'form',
      label: <CountedTab nameKey="shell.studio.entity.fields" count={count(value.fields)} />,
      children: wrap(
        <EntityFields
          fields={fields}
          readOnly={readOnly}
          origins={props.origins}
          warnings={props.warnings}
          onOriginsChange={props.onOriginsChange}
          onChange={onChange}
        />,
      ),
    },
    {
      key: 'validations',
      label: <CountedTab nameKey="shell.studio.entity.validations" count={count(value.validations)} />,
      children: wrap(
        <Validations validations={value.validations} fieldNames={fieldNames} readOnly={readOnly} onChange={onChange} />,
      ),
    },
    {
      key: 'indexes',
      label: <CountedTab nameKey="shell.studio.entity.indexes" count={count(value.indexes)} />,
      children: wrap(<Indexes indexes={value.indexes} fields={fields ?? []} readOnly={readOnly} onChange={onChange} />),
    },
  ]
}

/**
 * Proposed: the entity's header, under the page's title, which is its label: its description, then its name and display
 * field, with Edit details, which opens them with its label in a drawer. As everywhere in the editor, an edit goes into the
 * draft, and the save bar saves it.
 */
export function EntityHeader({
  value,
  onChange,
  readOnly = false,
}: Pick<EntityParts, 'value' | 'onChange' | 'readOnly'>) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  const labelText = useLabelText()
  const [open, setOpen] = useState(false)
  const fields = Array.isArray(value.fields) ? value.fields.filter(isContent) : []
  const fieldNames = fields.flatMap((field) =>
    typeof field.name === 'string' && field.name !== '' ? [field.name] : [],
  )
  const display = fields.find((field) => field.name === value.displayField)
  const description = labelText(value.description)
  const fact = (name: string, shown: ReactNode, testId: string) => (
    <span data-testid={testId}>
      <span style={{ color: colour.textMuted }}>{name}</span>
      <span style={{ marginInlineStart: spacing.xs }}>{shown}</span>
    </span>
  )

  return (
    <Flex justify="space-between" align="flex-start" gap={spacing.md} data-testid="studio-entity-header">
      <Flex vertical gap={spacing.xs}>
        {description ? <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text> : null}
        <Flex gap={spacing.lg} wrap>
          {fact(
            t('shell.studio.entity.name'),
            <Typography.Text code>{text(value.name)}</Typography.Text>,
            'studio-entity-name',
          )}
          {fact(
            t('shell.studio.entity.displayField'),
            display ? (labelText(display.label) ?? text(display.name)) : <EmptyDash />,
            'studio-entity-display-field',
          )}
        </Flex>
      </Flex>
      <AxisButton role="secondary" onClick={() => setOpen(true)}>
        {t(readOnly ? 'shell.studio.entity.details' : 'shell.studio.entity.editDetails')}
      </AxisButton>
      <Drawer
        open={open}
        size={560}
        title={t('shell.studio.entity.details')}
        onClose={() => setOpen(false)}
        data-testid="studio-entity-details"
      >
        <Flex vertical gap="middle">
          <div data-testid="studio-field-/name">
            <div>{t('shell.studio.entity.name')}</div>
            <Input
              aria-label={t('shell.studio.entity.name')}
              value={text(value.name)}
              onChange={(event) => onChange('/name', event.target.value)}
            />
          </div>
          <div data-testid="studio-field-/label">
            <div>{t('shell.studio.entity.label')}</div>
            {names !== undefined && label !== null && !readOnly ? (
              <LabelTexts pointer="/label" value={value.label} onChange={onChange} />
            ) : (
              <div data-testid="studio-field-/label/textKey">
                <Input
                  aria-label={t('shell.studio.labelKey')}
                  value={text(isContent(value.label) ? value.label.textKey : undefined)}
                  onChange={(event) => onChange('/label/textKey', event.target.value)}
                />
              </div>
            )}
          </div>
          {!readOnly && <ResourceDescription value={value.description} onChange={onChange} />}
          <Picker
            pointer="/displayField"
            label={t('shell.studio.entity.displayField')}
            options={fieldNames}
            value={value.displayField}
            required={false}
            onPick={(next) => onChange('/displayField', next)}
          />
        </Flex>
      </Drawer>
    </Flex>
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
}: {
  fields: unknown[] | undefined
  readOnly: boolean
  origins: FieldOrigin[] | undefined
  warnings: Map<number, FieldWarning> | undefined
  onOriginsChange: ((next: FieldOrigin[], touched?: string) => void) | undefined
  onChange: ChangeHandler
}) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const labelText = useLabelText()
  const [search, setSearch] = useState('')
  const [openIndex, setOpenIndex] = useState<number | null>(null)

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

  function addField() {
    onChange('/fields', [...fields!, { name: '', type: 'text' }])
    onOriginsChange?.([...rowOrigins(), null])
    setOpenIndex(fields!.length)
  }

  function removeField(index: number) {
    setOpenIndex(null)
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
          <Typography.Link
            data-testid={`studio-entity-field-open-${row.index}`}
            style={{ color: colour.primaryText }}
            onClick={() => setOpenIndex(row.index)}
          >
            {text(row.field.name) || t('shell.studio.entity.unnamedField')}
          </Typography.Link>
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
          main: readOnly ? undefined : (
            <AxisButton role="main" onClick={addField}>
              {t('shell.studio.entity.addField')}
            </AxisButton>
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
        onClose={() => setOpenIndex(null)}
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
          <div data-testid={`studio-field-${pointer}/name`}>
            <div>{t('shell.studio.entity.fieldName')}</div>
            <Input
              aria-label={t('shell.studio.entity.fieldName')}
              value={text(field.name)}
              onChange={(event) => onChange(`${pointer}/name`, event.target.value)}
            />
          </div>
          <div>
            <div>{t('shell.studio.entity.fieldLabel')}</div>
            <FieldLabel
              name={t('shell.studio.entity.fieldLabel')}
              pointer={`${pointer}/label`}
              value={field.label}
              onChange={onChange}
            />
          </div>
          <div data-testid={`studio-field-${pointer}/type`}>
            <div>{t('shell.studio.entity.fieldType')}</div>
            <Select
              aria-label={t('shell.studio.entity.fieldType')}
              showSearch
              optionFilterProp="label"
              virtual={false}
              style={{ width: '100%' }}
              value={typeof field.type === 'string' ? field.type : undefined}
              options={fieldTypes.map((option) => ({ value: option, label: t(`shell.studio.entity.type.${option}`) }))}
              onChange={(next: string) => onChange(pointer, changeFieldType(field, next))}
            />
          </div>
          <Flex gap="large">
            <FlagRow field={field} setting="required" pointer={`${pointer}/required`} onChange={onChange} />
            <FlagRow field={field} setting="unique" pointer={`${pointer}/unique`} onChange={onChange} />
          </Flex>
          <FieldLabel
            name={t('shell.studio.entity.help')}
            pointer={`${pointer}/help`}
            value={field.help}
            onChange={onChange}
          />
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

/** Required or Unique with its name beside the checkbox, as the drawer has room for. */
function FlagRow(props: { field: Content; setting: FieldSetting; pointer: string; onChange: ChangeHandler }) {
  const t = useText()
  if (!fieldSettings(text(props.field.type)).includes(props.setting)) {
    return null
  }
  return (
    <Flex gap="small" align="center">
      <Flag {...props} />
      <span>{t(`shell.studio.entity.${props.setting}`)}</span>
    </Flex>
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

/** The checkbox of `required` or `unique`, shown only when the field's type takes the setting. */
function Flag({
  field,
  setting,
  pointer,
  onChange,
}: {
  field: unknown
  setting: FieldSetting
  pointer: string
  onChange: ChangeHandler
}) {
  const t = useText()
  const content = isContent(field) ? field : {}
  if (!fieldSettings(text(content.type)).includes(setting)) {
    return null
  }
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Checkbox
        aria-label={t(`shell.studio.entity.${setting}`)}
        checked={content[setting] === true}
        onChange={(event) => onChange(pointer, event.target.checked)}
      />
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
