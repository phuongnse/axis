import { ArrowDownOutlined, ArrowUpOutlined, DeleteOutlined, WarningOutlined } from '@ant-design/icons'
import {
  Alert,
  Button,
  Checkbox,
  ConfigProvider,
  Drawer,
  Flex,
  Input,
  InputNumber,
  Select,
  Tag,
  Tooltip,
  Typography,
  DatePicker,
} from 'antd'
import dayjs from 'dayjs'
import { Fragment, lazy, Suspense, useContext, useState, type HTMLAttributes, type ReactNode } from 'react'
import { AxisButton } from '../platform/AxisButton'
import { AxisCheckboxField, AxisChoiceField, AxisField, AxisFieldGroup, AxisFieldRow } from '../platform/AxisField'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import { searchRows } from '../platform/tableControls'
import { useDesignTokens } from '../platform/themeMode'
import {
  changeFieldType,
  editableSettings,
  fieldSettings,
  fieldTypes,
  type FieldSetting,
  valueSourceOf,
  valueSources,
  withValueSource,
} from '../platform/entityFields'
import type { FieldOrigin, FieldWarning } from '../platform/entityMigrations'
import { escapePointerSegment } from '../platform/jsonPatch'
import {
  referenceOptions,
  StudioNamesContext,
  type ReferenceChoice,
  type ReferenceOption,
} from '../platform/studioNames'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { SectionPanel } from './FormSection'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { useTextOfKey } from './useTextOfKey'
import { type DesignerLayer, LayerMarkTag, RemovedRows } from './LayerMarkViews'
import { uniqueStepName } from './processPalette'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'
import { setOwn } from '../platform/ownProperty'
import { Link, useSearchParams } from 'react-router'
import { studioResourceAddress } from '../platform/studio'
import { booleanText } from '../platform/formatValue'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

type Content = Record<string, unknown>

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
function PickerControl({
  pointer,
  label,
  kind,
  options,
  value,
  required,
  block = false,
  onPick,
}: {
  pointer: string
  label: string
  kind?: string
  options?: readonly ReferenceOption[]
  value: unknown
  required: boolean
  /** Fills its row, as a field of a drawer does. */
  block?: boolean
  onPick: (next: string | undefined) => void
}) {
  const names = useContext(StudioNamesContext)
  const choices =
    options ??
    (names === undefined || kind === undefined ? undefined : referenceOptions(names, kind, undefined, pointer))
  return choices === undefined ? (
    <Input
      aria-label={label}
      aria-required={required || undefined}
      value={text(value)}
      onChange={(event) => onPick(event.target.value === '' && !required ? undefined : event.target.value)}
    />
  ) : (
    <ReferencePicker
      name={label}
      value={typeof value === 'string' ? value : undefined}
      required={required}
      options={choices}
      block={block}
      onChange={onPick}
    />
  )
}

/** A {@link PickerControl} with its plain label above it, as a row of the Validations list has. */
function Picker(props: Parameters<typeof PickerControl>[0]) {
  return (
    <div data-testid={`studio-field-${props.pointer}`}>
      <div>{props.label}</div>
      <PickerControl {...props} />
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
  /** The module entity's customization layer: its marks and Reset to module, which main built. */
  layer?: DesignerLayer
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
  // Proposed: the entity's own fields as choices of a picker: the name the file keeps and the label's text key, so every
  // picker of a field names it by its label, as `ReferencePicker` does for any choice.
  const fieldChoices: ReferenceChoice[] = (fields ?? []).flatMap((field) =>
    isContent(field) && typeof field.name === 'string' && field.name !== ''
      ? [
          {
            name: field.name,
            labelKey: isContent(field.label) && typeof field.label.textKey === 'string' ? field.label.textKey : null,
          },
        ]
      : [],
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
        <EntityGeneral value={value} onChange={onChange} readOnly={readOnly} fieldChoices={fieldChoices} />
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
          validations={value.validations}
          readOnly={readOnly}
          origins={props.origins}
          warnings={props.warnings}
          onOriginsChange={props.onOriginsChange}
          layer={props.layer}
          onChange={onChange}
          openIndex={openField}
          onOpen={setOpenField}
        />
      </SectionPanel>
      <SectionPanel {...section('validations')}>
        <Validations
          validations={value.validations}
          fieldChoices={fieldChoices}
          readOnly={readOnly}
          layer={props.layer}
          onChange={onChange}
        />
      </SectionPanel>
      <SectionPanel {...section('indexes')}>
        <Indexes
          indexes={value.indexes}
          fields={fields ?? []}
          readOnly={readOnly}
          layer={props.layer}
          onChange={onChange}
        />
      </SectionPanel>
    </Flex>
  )
  return readOnly ? <ConfigProvider componentDisabled>{designer}</ConfigProvider> : designer
}

/** The text of a label or description as `useTextOfKey` gives it, else its text key. */
function useLabelText(): (value: unknown) => string | null {
  const textOfKey = useTextOfKey()
  return (value) => {
    const key = isContent(value) && typeof value.textKey === 'string' ? value.textKey : null
    if (key === null || key === '') {
      return null
    }
    return textOfKey(key) ?? key
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
  fieldChoices,
}: {
  value: Content
  onChange: ChangeHandler
  readOnly: boolean
  fieldChoices: readonly ReferenceChoice[]
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
          options={fieldChoices}
          block
          onChange={(next) => onChange('/displayField', next)}
        />
      </AxisField>
    </>
  )
}

/** The types whose Type cell names the entity they point to, as "Reference to Department". */
const typesWithTarget = new Set(['reference', 'references', 'child-collection'])

/**
 * Proposed: what a field's type says in the Type column, in words: the type, and for a type that points to an entity,
 * that entity by its label, as "Reference to Department" or "Child collection of Line item", as the target picker in the
 * drawer names it. The name shows only where the entity has no label. The entity is a link that opens it, a module's
 * entity at its module view, through the leave guard as every link is. Enum and the other types are their name alone,
 * with no count, as a list shows no count that leads nowhere.
 */
function TypeCell({ field }: { field: Content }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  const textOfKey = useTextOfKey()
  const application = useSearchParams()[0].get('app')
  const type = text(field.type)
  if (type === '') {
    return <EmptyDash />
  }
  const target = text(field.target)
  if (target === '' || !typesWithTarget.has(type)) {
    return <span>{t(`shell.studio.entity.type.${type}`)}</span>
  }
  const entity = names?.entities.find((item) => item.name.toLowerCase() === target.toLowerCase())
  const choice = names
    ? referenceOptions(names, 'entity', undefined, '').find((option) => option.name === entity?.name)
    : undefined
  const words = textOfKey(choice?.labelKey) ?? target
  const [before, after = ''] = t(`shell.studio.entity.typeOf.${type}`).split('{entity}')
  return (
    <span>
      {before}
      {entity && application ? (
        <Link to={studioResourceAddress(application, entity.id)} style={{ color: colour.primaryText }}>
          {words}
        </Link>
      ) : (
        words
      )}
      {after}
    </span>
  )
}

/**
 * Proposed: the settings that set a field apart, as neutral tags in the Properties column: where its value comes from
 * (Computed, Sequence, Creator, Matched, a User id, Default), then Searchable and Sensitive.
 */
function fieldTraits(field: Content): string[] {
  return [
    typeof field.expression === 'string' && field.expression !== '' ? 'shell.studio.entity.computed' : null,
    typeof field.sequence === 'string' && field.sequence !== '' ? 'shell.studio.entity.sequence' : null,
    field.creator === true ? 'shell.studio.entity.creator' : null,
    isContent(field.match) ? 'shell.studio.entity.matched' : null,
    field.userId === true ? 'shell.studio.entity.userId' : null,
    field.default !== undefined ? 'shell.studio.entity.default' : null,
    field.searchable === true ? 'shell.studio.entity.searchable' : null,
    field.sensitive === true ? 'shell.studio.entity.sensitive' : null,
  ].filter((key): key is string => key !== null)
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
  validations,
  readOnly,
  origins,
  warnings,
  onOriginsChange,
  layer,
  onChange,
  openIndex,
  onOpen,
}: {
  fields: unknown[] | undefined
  validations: unknown
  readOnly: boolean
  origins: FieldOrigin[] | undefined
  warnings: Map<number, FieldWarning> | undefined
  onOriginsChange: ((next: FieldOrigin[], touched?: string) => void) | undefined
  layer: DesignerLayer | undefined
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
      kind: 'fixed',
      title: t('shell.studio.entity.fieldName'),
      searchText: (row) => text(row.field.name),
      render: (_, row) => (
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
          {layer && <LayerMarkTag layer={layer} list="/fields" index={row.index} name={text(row.field.name)} />}
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
      kind: 'freeText',
      title: t('shell.studio.entity.fieldLabel'),
      searchText: (row) => labelText(row.field.label) ?? '',
      render: (_, row) => labelText(row.field.label) ?? <EmptyDash />,
    },
    {
      key: 'type',
      kind: 'fixed',
      title: t('shell.studio.entity.fieldType'),
      render: (_, row) => <TypeCell field={row.field} />,
    },
    {
      key: 'required',
      kind: 'fixed',
      title: t('shell.studio.entity.required'),
      render: (_, row) =>
        // Proposed: a yes-or-no setting reads Yes or No, as a record's boolean does, never a mark beside a dash.
        booleanText(row.field.required === true, t),
    },
    {
      key: 'unique',
      kind: 'fixed',
      title: t('shell.studio.entity.unique'),
      render: (_, row) => booleanText(row.field.unique === true, t),
    },
    {
      key: 'properties',
      kind: 'freeText',
      priority: 'low',
      title: t('shell.studio.entity.properties'),
      render: (_, row) => {
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
        dataSource={shown}
        rowKey={(row) => row.index}
        columns={columns}
        rowAttributes={(row) => ({ 'data-testid': `studio-field-/fields/${row.index}` }) as HTMLAttributes<HTMLElement>}
        rowActions={
          readOnly
            ? undefined
            : {
                configured: 3,
                actions: (row) => [
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
      <RemovedRows layer={layer} list="/fields" />
      <FieldDrawer
        index={open}
        fields={fields}
        validations={validations}
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
  validations,
  readOnly,
  warning,
  onChange,
  onClose,
}: {
  index: number | null
  fields: unknown[]
  /** The entity's validations, of which the drawer shows those at this field. */
  validations: unknown
  readOnly: boolean
  warning: FieldWarning | undefined
  onChange: ChangeHandler
  onClose: () => void
}) {
  const t = useText()
  const { spacing } = useDesignTokens()
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
        <Flex vertical gap={spacing.lg}>
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
          <AxisFieldGroup title={t('shell.studio.entity.group.general')} first testId="studio-field-group-general">
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
                options={fieldTypes.map((option) => ({
                  value: option,
                  label: t(`shell.studio.entity.type.${option}`),
                }))}
                onChange={(next: string) => onChange(pointer, changeFieldType(field, next))}
              />
            </AxisField>
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
          </AxisFieldGroup>
          <FieldGroups
            index={index}
            field={field}
            fields={fields}
            validations={validations}
            readOnly={readOnly}
            onChange={onChange}
          />
        </Flex>
      )}
    </Drawer>
  )
}

/**
 * Proposed: a field's settings after General, in groups, each setting with its help, one to a row, and only those its
 * type takes, so a group with none is left out:
 * - the type's settings: the target entity, the values and their labels, the currency, and precision with scale;
 * - Validation: Required, Unique, the length and the range, each pair on one row, a file's size and media types, then the
 *   rules whose message shows at this field;
 * - Value: where the value comes from, one choice of the ways the type takes, then only that way's settings: a default
 *   and whether it holds a user for an entered value, the expression, the sequence, or the match;
 * - Search and privacy: Searchable, Sensitive and Exact lookup.
 */
function FieldGroups({
  index,
  field,
  fields,
  validations,
  readOnly,
  onChange,
}: {
  index: number
  field: Content
  fields: unknown[]
  validations: unknown
  readOnly: boolean
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/fields/${index}`
  const taken = editableSettings(field)
  const has = (setting: FieldSetting) => taken.includes(setting)
  const key = (setting: string) => `shell.studio.field.entity.fields.${setting}`
  const label = (setting: string) => t(key(setting))
  const about = (setting: FieldSetting, required = false) => ({
    label: label(setting),
    help: helpOf(t, key(setting)),
    required,
    testId: `studio-field-${pointer}/${setting}`,
  })
  const number = (setting: 'maxLength' | 'minLength' | 'precision' | 'scale' | 'maxSize') => (
    <AxisField {...about(setting)}>
      <InputNumber
        aria-label={label(setting)}
        precision={0}
        min={setting === 'maxSize' || setting === 'minLength' ? 1 : 0}
        style={{ width: '100%' }}
        value={typeof field[setting] === 'number' ? field[setting] : null}
        onChange={(next) => onChange(`${pointer}/${setting}`, typeof next === 'number' ? next : undefined)}
      />
    </AxisField>
  )
  const limit = (setting: 'min' | 'max') => (
    <AxisField {...about(setting)}>
      <Limit
        type={text(field.type)}
        value={field[setting]}
        label={label(setting)}
        onChange={(next) => onChange(`${pointer}/${setting}`, next)}
      />
    </AxisField>
  )
  // A false creator or sensitive is written, as before; Searchable and Exact lookup are removed when off, as even a
  // false exactLookup is refused on a field that is not sensitive.
  const flag = (setting: 'creator' | 'sensitive' | 'exactLookup' | 'searchable') => (
    <AxisCheckboxField
      label={label(setting)}
      help={helpOf(t, key(setting))}
      checked={field[setting] === true}
      testId={`studio-field-${pointer}/${setting}`}
      onChange={(checked) =>
        onChange(
          `${pointer}/${setting}`,
          (setting === 'exactLookup' || setting === 'searchable') && !checked ? undefined : checked,
        )
      }
    />
  )
  const tags = (setting: 'values' | 'accept') => (
    <AxisField {...about(setting, setting === 'values')}>
      <Select
        mode="tags"
        aria-label={label(setting)}
        virtual={false}
        style={{ width: '100%' }}
        value={
          Array.isArray(field[setting]) ? (field[setting] as unknown[]).filter((item) => typeof item === 'string') : []
        }
        onChange={(next: string[]) => onChange(`${pointer}/${setting}`, next.length === 0 ? undefined : next)}
      />
    </AxisField>
  )
  const pair = (left: ReactNode, right: ReactNode) =>
    left && right ? (
      <AxisFieldRow>
        {left}
        {right}
      </AxisFieldRow>
    ) : (
      (left ?? right)
    )
  const typeName = t(`shell.studio.entity.type.${text(field.type)}`)
  const typeSettings = [
    has('target') && (
      <AxisField key="target" {...about('target', true)}>
        <PickerControl
          pointer={`${pointer}/target`}
          label={label('target')}
          kind="entity"
          value={field.target}
          required
          block
          onPick={(next) => onChange(`${pointer}/target`, next ?? '')}
        />
      </AxisField>
    ),
    has('values') && <Fragment key="values">{tags('values')}</Fragment>,
    has('valueLabels') && (
      <AxisField key="valueLabels" {...about('valueLabels')}>
        <ValueLabels pointer={`${pointer}/valueLabels`} field={field} onChange={onChange} />
      </AxisField>
    ),
    has('currency') && (
      <CurrencySource
        key="currency"
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
    ),
    (has('precision') || has('scale')) && (
      <Fragment key="precision">
        {pair(has('precision') ? number('precision') : null, has('scale') ? number('scale') : null)}
      </Fragment>
    ),
  ].filter(Boolean)
  // Proposed: where the value comes from is one choice, and only the chosen way's settings follow it, so the drawer
  // never offers two ways the compiler refuses together. A type with one way shows no choice.
  const type = text(field.type)
  const sources = valueSources(type)
  const source = valueSourceOf(field)
  const sourceKey = (way: string) => `shell.studio.entity.valueSource.${way}`
  const valueSettings = [
    sources.length > 1 && (
      <AxisChoiceField
        key="source"
        label={label('valueSource')}
        help={helpOf(t, key('valueSource'))}
        value={source}
        disabled={readOnly}
        testId={`studio-field-${pointer}/valueSource`}
        options={sources.map((way) => ({ value: way, label: t(sourceKey(way)), help: t(`${sourceKey(way)}.help`) }))}
        onChange={(next) => onChange(pointer, withValueSource(field, next))}
      />
    ),
    source === 'entered' && has('default') && (
      <AxisField key="default" {...about('default')}>
        <Expression
          pointer={`${pointer}/default`}
          name={label('default')}
          value={text(field.default)}
          required={false}
          readOnly={readOnly}
          onChange={onChange}
        />
      </AxisField>
    ),
    source === 'entered' && type === 'text' && (
      <AxisCheckboxField
        key="userId"
        label={label('userId')}
        help={helpOf(t, key('userId'))}
        checked={field.userId === true}
        testId={`studio-field-${pointer}/userId`}
        onChange={(checked) => onChange(`${pointer}/userId`, checked ? true : undefined)}
      />
    ),
    source === 'computed' && (
      <AxisField key="expression" {...about('expression', true)}>
        <Expression
          pointer={`${pointer}/expression`}
          name={label('expression')}
          value={text(field.expression)}
          required
          readOnly={readOnly}
          onChange={onChange}
        />
      </AxisField>
    ),
    source === 'sequence' && (
      <AxisField key="sequence" {...about('sequence', true)}>
        <PickerControl
          pointer={`${pointer}/sequence`}
          label={label('sequence')}
          kind="sequence"
          value={field.sequence}
          required
          block
          onPick={(next) => onChange(`${pointer}/sequence`, next ?? '')}
        />
      </AxisField>
    ),
    source === 'matched' && (
      <MatchSettings key="match" pointer={pointer} field={field} fields={fields} onChange={onChange} />
    ),
  ].filter(Boolean)
  // Proposed: Exact lookup shows once the field is sensitive, as it needs that, or while the file still has it.
  const searchSettings = (['searchable', 'sensitive', 'exactLookup'] as const)
    .filter((setting) => has(setting))
    .filter((setting) => setting !== 'exactLookup' || field.sensitive === true || field.exactLookup !== undefined)
    .map((setting) => <Fragment key={setting}>{flag(setting)}</Fragment>)

  return (
    <>
      {typeSettings.length > 0 && (
        <AxisFieldGroup
          title={t('shell.studio.entity.group.type').replace('{type}', typeName)}
          testId="studio-field-group-type"
        >
          {typeSettings}
        </AxisFieldGroup>
      )}
      <AxisFieldGroup title={t('shell.studio.entity.group.validation')} testId="studio-field-group-validation">
        {/* Proposed: a value the server sets is never required of people, so Required shows only for an entered value,
            or while the file still has it, for the author to clear. */}
        {(source === 'entered' || field.required === true) && (
          <FlagRow field={field} setting="required" pointer={`${pointer}/required`} onChange={onChange} />
        )}
        <FlagRow field={field} setting="unique" pointer={`${pointer}/unique`} onChange={onChange} />
        {pair(has('minLength') ? number('minLength') : null, has('maxLength') ? number('maxLength') : null)}
        {pair(has('min') ? limit('min') : null, has('max') ? limit('max') : null)}
        {has('maxSize') && number('maxSize')}
        {has('accept') && tags('accept')}
        <FieldRules fieldName={text(field.name)} validations={validations} readOnly={readOnly} onChange={onChange} />
      </AxisFieldGroup>
      {valueSettings.length > 0 && (
        <AxisFieldGroup title={t('shell.studio.entity.group.value')} testId="studio-field-group-value">
          {valueSettings}
        </AxisFieldGroup>
      )}
      {searchSettings.length > 0 && (
        <AxisFieldGroup title={t('shell.studio.entity.group.search')} testId="studio-field-group-search">
          {searchSettings}
        </AxisFieldGroup>
      )}
    </>
  )
}

/**
 * Proposed: the settings of a matched reference: Find by, a unique field of the target entity, and From, a field of this
 * entity whose value is looked up there. Find by offers only the target's unique fields, and From only this entity's
 * fields of the same type, and for a reference the same target, that are not the field itself, sensitive, computed or
 * matched, as the compiler allows no other (AXC0362). Both are named by their labels, as every picker names a choice.
 */
function MatchSettings({
  pointer,
  field,
  fields,
  onChange,
}: {
  pointer: string
  field: Content
  fields: unknown[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const key = (setting: string) => `shell.studio.field.entity.fields.${setting}`
  const match = isContent(field.match) ? field.match : {}
  const target = text(field.target).toLowerCase()
  const targetFields = names?.entities.find((entity) => entity.name.toLowerCase() === target)?.fields ?? []
  const findBy = targetFields.find((candidate) => candidate.name === text(match.field))
  const findByChoices: ReferenceChoice[] = targetFields
    .filter((candidate) => candidate.unique === true)
    .map((candidate) => ({ name: candidate.name, labelKey: candidate.labelKey ?? null }))
  const fromChoices: ReferenceChoice[] = fields.flatMap((other) =>
    isContent(other) &&
    typeof other.name === 'string' &&
    other.name !== '' &&
    other.name !== field.name &&
    other.sensitive !== true &&
    typeof other.expression !== 'string' &&
    !isContent(other.match) &&
    (findBy === undefined ||
      (other.type === findBy.type && (findBy.type !== 'reference' || text(other.target) === (findBy.target ?? ''))))
      ? [{ name: other.name, labelKey: isContent(other.label) ? text(other.label.textKey) || null : null }]
      : [],
  )
  const write = (part: 'field' | 'from', next: string | undefined) =>
    onChange(`${pointer}/match`, { ...match, [part]: next ?? '' })
  return (
    <>
      <AxisField
        label={t(key('matchField'))}
        help={helpOf(t, key('matchField'))}
        required
        testId={`studio-field-${pointer}/match/field`}
      >
        <PickerControl
          pointer={`${pointer}/match/field`}
          label={t(key('matchField'))}
          options={findByChoices}
          value={match.field}
          required
          block
          onPick={(next) => write('field', next)}
        />
      </AxisField>
      <AxisField
        label={t(key('matchFrom'))}
        help={helpOf(t, key('matchFrom'))}
        required
        testId={`studio-field-${pointer}/match/from`}
      >
        <PickerControl
          pointer={`${pointer}/match/from`}
          label={t(key('matchFrom'))}
          options={fromChoices}
          value={match.from}
          required
          block
          onPick={(next) => write('from', next)}
        />
      </AxisField>
    </>
  )
}

/**
 * Proposed: the rules whose message shows at this field, the entity's validations with this `field`, each in a box with
 * its name, its condition, the expression that must be true for a record to save, and its message, typed as texts as a
 * label is. Add rule adds one at this field, named as Add validation names it, and Remove rule removes it. A rule may read
 * any field, so the entity's Validations section lists every rule too.
 */
function FieldRules({
  fieldName,
  validations,
  readOnly,
  onChange,
}: {
  fieldName: string
  validations: unknown
  readOnly: boolean
  onChange: ChangeHandler
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  const resource = useContext(LabelTextsContext)
  const typesTexts = names !== undefined && resource !== null && !readOnly
  if (validations !== undefined && !Array.isArray(validations)) {
    return null
  }
  const list: unknown[] = validations ?? []
  const here = list.flatMap((rule, position) =>
    isContent(rule) && fieldName !== '' && rule.field === fieldName ? [{ rule, position }] : [],
  )
  const key = (name: string) => `shell.studio.field.entity.validations.${name}`
  return (
    <AxisField label={t(key('rules'))} help={helpOf(t, key('rules'))} testId="studio-field-rules">
      <Flex vertical gap={spacing.sm}>
        {here.map(({ rule, position }) => {
          const pointer = `/validations/${position}`
          return (
            <Flex
              key={position}
              vertical
              gap={spacing.md}
              data-testid={`studio-field-${pointer}`}
              style={{ border: `1px solid ${colour.borderSubtle}`, borderRadius: radius.base, padding: spacing.md }}
            >
              <Flex justify="space-between" align="center">
                <Typography.Text code data-testid={`studio-field-${pointer}/name`}>
                  {text(rule.name)}
                </Typography.Text>
                {readOnly ? null : (
                  <AxisButton
                    role="icon"
                    icon={<DeleteOutlined />}
                    nameKey="shell.studio.entity.removeRule"
                    destructive
                    onClick={() => onChange(pointer, undefined)}
                  />
                )}
              </Flex>
              <AxisField
                label={t(key('expression'))}
                help={helpOf(t, key('expression'))}
                required
                testId={`studio-field-${pointer}/expression`}
              >
                <Expression
                  pointer={`${pointer}/expression`}
                  name={t(key('expression'))}
                  value={text(rule.expression)}
                  required
                  readOnly={readOnly}
                  onChange={onChange}
                />
              </AxisField>
              <AxisField
                label={t(key('message'))}
                help={helpOf(t, key('message'))}
                required
                testId={typesTexts ? `studio-field-${pointer}/message` : undefined}
              >
                {typesTexts ? (
                  <LabelTexts pointer={`${pointer}/message`} value={rule.message} onChange={onChange} />
                ) : (
                  <FieldLabel
                    name={t(key('message'))}
                    pointer={`${pointer}/message`}
                    value={rule.message}
                    onChange={onChange}
                  />
                )}
              </AxisField>
            </Flex>
          )
        })}
        {readOnly ? null : (
          <div>
            <AxisButton
              role="secondary"
              disabled={fieldName === ''}
              onClick={() =>
                onChange('/validations', [
                  ...list,
                  {
                    name: uniqueStepName(list, 'validation'),
                    expression: '',
                    message: { textKey: '' },
                    field: fieldName,
                  },
                ])
              }
            >
              {t('shell.studio.entity.addRule')}
            </AxisButton>
          </div>
        )}
      </Flex>
    </AxisField>
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
        style={{ width: '100%' }}
        value={typeof value === 'string' && dayjs(value).isValid() ? dayjs(value) : null}
        onChange={(date) => onChange(date ? date.format('YYYY-MM-DD') : undefined)}
      />
    )
  }
  if (type === 'date-time') {
    return (
      <DatePicker
        aria-label={label}
        style={{ width: '100%' }}
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
      style={{ width: '100%' }}
      precision={type === 'integer' ? 0 : undefined}
      value={typeof value === 'number' ? value : null}
      onChange={(next) => onChange(typeof next === 'number' ? next : undefined)}
    />
  )
}

/**
 * Proposed: where a money field's currency comes from, as a choice between a fixed code and another field of the record,
 * then the code or the field. The field holds only the one chosen: Field writes an empty `currencyField` and removes
 * `currency`, and Fixed code removes `currencyField`. A typed code is turned into upper case.
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
  const key = (setting: string) => `shell.studio.field.entity.fields.${setting}`
  function switchTo(next: 'code' | 'field') {
    if ((next === 'field') === fromField) {
      return
    }
    const rebuilt: Content = {}
    for (const [name, value] of Object.entries(field)) {
      if (name !== 'currency' && name !== 'currencyField') {
        setOwn(rebuilt, name, value)
      }
    }
    if (next === 'field') {
      rebuilt.currencyField = ''
    }
    onChange(pointer, rebuilt)
  }
  return (
    <>
      <AxisChoiceField
        label={t(key('currencySource'))}
        help={helpOf(t, key('currencySource'))}
        value={fromField ? 'field' : 'code'}
        options={[
          { value: 'code', label: t('shell.studio.entity.currencyFixed') },
          { value: 'field', label: t('shell.studio.entity.currencyFromField') },
        ]}
        onChange={switchTo}
      />
      {fromField ? (
        <AxisField
          label={t(key('currencyField'))}
          help={helpOf(t, key('currencyField'))}
          required
          testId={`studio-field-${pointer}/currencyField`}
        >
          <PickerControl
            pointer={`${pointer}/currencyField`}
            label={t(key('currencyField'))}
            options={currencyFields}
            value={field.currencyField}
            required
            block
            onPick={(next) => onChange(`${pointer}/currencyField`, next ?? '')}
          />
        </AxisField>
      ) : (
        <AxisField
          label={t(key('currency'))}
          help={helpOf(t, key('currency'))}
          testId={`studio-field-${pointer}/currency`}
        >
          <Input
            aria-label={t(key('currency'))}
            value={text(field.currency)}
            onChange={(event) =>
              onChange(`${pointer}/currency`, event.target.value === '' ? undefined : event.target.value.toUpperCase())
            }
          />
        </AxisField>
      )}
    </>
  )
}

/**
 * A label picker for each value of an enum field. Each change writes the whole `valueLabels` map, in the order of
 * `values`, and the last cleared label removes the key.
 */
function ValueLabels({ pointer, field, onChange }: { pointer: string; field: Content; onChange: ChangeHandler }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const names = useContext(StudioNamesContext)
  const resource = useContext(LabelTextsContext)
  const readOnly = ConfigProvider.useConfig().componentDisabled ?? false
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

  // Proposed: with the folder's names, each value is a row of the value as stored and its label typed as texts, a box per
  // locale, as every label of the designer is. Without them, each value has a picker of text keys.
  if (names !== undefined && resource !== null && !readOnly) {
    return (
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'max-content minmax(0, 1fr)',
          columnGap: spacing.md,
          rowGap: spacing.xs,
          alignItems: 'start',
        }}
      >
        {values.map((value) => {
          const at = `${pointer}/${escapePointerSegment(value)}`
          return [
            <Typography.Text key={`${value}/value`} code style={{ marginTop: 5 }}>
              {value}
            </Typography.Text>,
            <div key={`${value}/label`} data-testid={`studio-field-${at}`}>
              <LabelTexts pointer={at} value={labels[value]} onChange={onChange} />
            </div>,
          ]
        })}
      </div>
    )
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
              {layer && <LayerMarkTag layer={layer} list="/indexes" index={position} name={text(content.name)} />}
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
        <RemovedRows layer={layer} list="/indexes" />
      </Flex>
    </div>
  )
}

/** The validations of the entity as rows of a name, an expression, a message key and a field. */
function Validations({
  validations,
  fieldChoices,
  readOnly,
  layer,
  onChange,
}: {
  validations: unknown
  fieldChoices: readonly ReferenceChoice[]
  readOnly: boolean
  layer: DesignerLayer | undefined
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
              {layer && <LayerMarkTag layer={layer} list="/validations" index={index} name={text(content.name)} />}
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
                options={fieldChoices}
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
        <RemovedRows layer={layer} list="/validations" />
      </Flex>
    </div>
  )
}
