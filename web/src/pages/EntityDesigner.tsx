import {
  Alert,
  Button,
  Checkbox,
  ConfigProvider,
  Flex,
  Input,
  InputNumber,
  Radio,
  Select,
  Switch,
  Typography,
  DatePicker,
} from 'antd'
import dayjs from 'dayjs'
import { useContext } from 'react'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
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
import { LabelTexts } from './LabelTexts'
import { type DesignerLayer, LayerMarkTag, RemovedRows } from './LayerMarkViews'
import { Expression } from './ProcessFields'
import { uniqueStepName } from './processPalette'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'
import { setOwn } from '../platform/ownProperty'

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

/** The label and the help of an entity's input, from `shell.studio.field.entity.<path>`, see docs/reference/frontend.md#platform-field-help. */
function fieldTexts(t: (key: string) => string, path: string) {
  return { label: t(`shell.studio.field.entity.${path}`), help: t(`shell.studio.field.entity.${path}.help`) }
}

/** The title and the help of an entity's group of inputs, which has no one input to label. */
function groupTexts(t: (key: string) => string, path: string) {
  const { label, help } = fieldTexts(t, path)
  return { title: label, help }
}

/**
 * A picker over `options`, or over the application's names of `kind` when `options` is not given. It is a plain box
 * while the application's names have not loaded and `options` is not given.
 */
function Picker({
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
      <AxisField label={label} help={help}>
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
}

/**
 * The designer of an entity. It shows the entity's name, label and display field, then its fields as a table of
 * name, label, type, required, unique and a settings column that shows only the settings the field's type takes,
 * and its validations as rows of a name, an expression, a message key and a field. A field can be added, removed and moved,
 * and a type change drops the settings the new type does not take. A computed field's `expression` and each
 * validation's expression use the expression editor, and so does a field's `default`, which has no field in scope.
 * A field's settings also include `min` and `max` in the type of the field, `minLength` and `searchable`, and they
 * hide `default`, `min`, `max` and `minLength` next to an `expression`, a `sequence` or `creator`. An Indexes list
 * under the fields adds, moves and removes the entity's `indexes`, each a name, a list of the entity's fields and a
 * Unique choice. The name of an index or a validation is shown as plain text, and an added one gets `index1` or
 * `validation1`, the first name its list does not use. The designer edits the draft through `onChange`, so a key it
 * does not show, such as `id`, `kind` or an unknown key on a field, stays as it is. Every input has the id
 * `studio-field-<pointer>`, and each row of the table has the id of its field, so a diagnostic opens at its input or
 * at the row of a setting that the row does not show. Clearing an optional value removes its key. With `readOnly`,
 * as for an entity of a module, every input is off, the label is its plain text key, and there are no buttons that
 * add, remove or move a field or a validation. With `origins`, the loaded name of each row of the fields table or
 * `null` for a row added in this session, Add field, Remove and Move report the origins that follow through
 * `onOriginsChange`, and Remove also reports the origin of the removed row as touched. With `warnings`, the row
 * numbers of fields whose change no migration follows, a warning shows in the settings column of that row. With `layer`,
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
  const fieldNames = (fields ?? []).flatMap((field) =>
    isContent(field) && typeof field.name === 'string' && field.name !== '' ? [field.name] : [],
  )

  const designer = (
    <Flex vertical gap="middle" data-testid="studio-entity-designer">
      <div data-testid="studio-field-/name">
        <AxisField {...fieldTexts(t, 'name')}>
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
        onPick={(next) => onChange('/displayField', next)}
      />
      <Fields
        fields={fields}
        readOnly={readOnly}
        origins={origins}
        warnings={warnings}
        onOriginsChange={onOriginsChange}
        layer={layer}
        onChange={onChange}
      />
      <Indexes indexes={value.indexes} fields={fields ?? []} readOnly={readOnly} layer={layer} onChange={onChange} />
      <Validations
        validations={value.validations}
        fieldNames={fieldNames}
        readOnly={readOnly}
        layer={layer}
        onChange={onChange}
      />
    </Flex>
  )

  return readOnly ? <ConfigProvider componentDisabled>{designer}</ConfigProvider> : designer
}

interface FieldRow {
  index: number
  field: unknown
}

/** The fields of the entity as a table, with Add field below it. */
function Fields({
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

  if (fields === undefined) {
    return (
      <div data-testid="studio-field-/fields">
        <JsonOnly />
      </div>
    )
  }

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

  /** The origin of each row, `null` for a row that has none. */
  function rowOrigins(): FieldOrigin[] {
    return fields!.map((_, index) => origins?.[index] ?? null)
  }

  function addField() {
    onChange('/fields', [...fields!, { name: '', type: 'text' }])
    onOriginsChange?.([...rowOrigins(), null])
  }

  function removeField(index: number) {
    onChange(
      '/fields',
      fields!.filter((_, position) => position !== index),
    )
    onOriginsChange?.(
      rowOrigins().filter((_, position) => position !== index),
      origins?.[index] ?? undefined,
    )
  }

  const columns: AxisColumn<FieldRow>[] = [
    {
      key: 'name',
      kind: 'freeText',
      title: t('shell.studio.entity.fieldName'),
      render: (_, { field, index }) => {
        const name = text(isContent(field) ? field.name : undefined)
        return (
          <Flex vertical gap="small">
            <div data-testid={`studio-field-/fields/${index}/name`}>
              <Input
                aria-label={t('shell.studio.entity.fieldName')}
                value={name}
                onChange={(event) => onChange(`/fields/${index}/name`, event.target.value)}
              />
            </div>
            {layer && <LayerMarkTag layer={layer} list="/fields" index={index} name={name} />}
          </Flex>
        )
      },
    },
    {
      key: 'label',
      kind: 'freeText',
      title: t('shell.studio.entity.fieldLabel'),
      render: (_, { field, index }) => (
        <FieldLabel
          name={t('shell.studio.entity.fieldLabel')}
          pointer={`/fields/${index}/label`}
          value={isContent(field) ? field.label : undefined}
          onChange={onChange}
        />
      ),
    },
    {
      key: 'type',
      kind: 'fixed',
      title: t('shell.studio.entity.fieldType'),
      render: (_, { field, index }) => {
        const content = isContent(field) ? field : {}
        return (
          <div data-testid={`studio-field-/fields/${index}/type`}>
            <Select
              aria-label={t('shell.studio.entity.fieldType')}
              showSearch
              optionFilterProp="label"
              virtual={false}
              style={{ minWidth: 160 }}
              value={typeof content.type === 'string' ? content.type : undefined}
              options={fieldTypes.map((option) => ({ value: option, label: option }))}
              onChange={(next: string) => onChange(`/fields/${index}`, changeFieldType(content, next))}
            />
          </div>
        )
      },
    },
    {
      key: 'required',
      kind: 'fixed',
      title: t('shell.studio.entity.required'),
      render: (_, { field, index }) => (
        <Flag field={field} setting="required" pointer={`/fields/${index}/required`} onChange={onChange} />
      ),
    },
    {
      key: 'unique',
      kind: 'fixed',
      title: t('shell.studio.entity.unique'),
      render: (_, { field, index }) => (
        <Flag field={field} setting="unique" pointer={`/fields/${index}/unique`} onChange={onChange} />
      ),
    },
    {
      key: 'settings',
      kind: 'freeText',
      title: t('shell.studio.entity.settings'),
      render: (_, { field, index }) => (
        <Flex vertical gap="small">
          <AxisField {...fieldTexts(t, 'fields.helpText')}>
            <FieldLabel
              name={fieldTexts(t, 'fields.helpText').label}
              pointer={`/fields/${index}/help`}
              value={isContent(field) ? field.help : undefined}
              onChange={onChange}
            />
          </AxisField>
          <Settings
            index={index}
            field={isContent(field) ? field : {}}
            fields={fields}
            readOnly={readOnly}
            onChange={onChange}
          />
          {warnings?.get(index) && (
            // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
            <Alert
              type="warning"
              data-testid={`studio-migration-warning-/fields/${index}`}
              title={t(
                warnings.get(index) === 'enum'
                  ? 'shell.studio.entity.migration.enumWarning'
                  : 'shell.studio.entity.migration.uniqueWarning',
              )}
            />
          )}
        </Flex>
      ),
    },
    ...(readOnly
      ? []
      : [
          {
            key: 'actions',
            kind: 'fixed' as const,
            render: (_: unknown, { index }: FieldRow) => (
              <Flex gap="small">
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                <Button disabled={index === 0} onClick={() => move(index, -1)}>
                  {t('shell.studio.entity.moveUp')}
                </Button>
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                <Button disabled={index === fields.length - 1} onClick={() => move(index, 1)}>
                  {t('shell.studio.entity.moveDown')}
                </Button>
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                <Button danger onClick={() => removeField(index)}>
                  {t('shell.studio.entity.removeField')}
                </Button>
              </Flex>
            ),
          },
        ]),
  ]

  return (
    <div data-testid="studio-field-/fields">
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.entity.fields')}</Typography.Text>
        <AxisTable<FieldRow>
          dataSource={fields.map((field, index) => ({ field, index }))}
          rowKey={(row) => String(row.index)}
          emptyText={t('shell.table.empty')}
          rowAttributes={(row) =>
            ({ 'data-testid': `studio-field-/fields/${row.index}` }) as React.HTMLAttributes<HTMLElement>
          }
          columns={columns}
        />
        {readOnly ? null : (
          <div>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={addField}>{t('shell.studio.entity.addField')}</Button>
          </div>
        )}
        <RemovedRows layer={layer} list="/fields" />
      </Flex>
    </div>
  )
}

/** The text key picker of a field's label or help, named `name`. Clearing it removes the text. */
function FieldLabel({
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

  function flag(setting: 'creator' | 'sensitive' | 'exactLookup') {
    return (
      <Checkbox
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
            {...settingTexts(setting)}
            kind={setting === 'target' ? 'entity' : 'sequence'}
            value={field[setting]}
            required={setting === 'target'}
            onPick={(next) => onChange(`${pointer}/${setting}`, next ?? (setting === 'target' ? '' : undefined))}
          />
        ) : (
          <div key={setting} data-testid={`studio-field-${pointer}/${setting}`}>
            {setting === 'valueLabels' ? input(setting) : labelled(setting)}
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
      <AxisField {...source}>
        <Radio.Group
          role="radiogroup"
          aria-label={source.label}
          value={fromField ? 'field' : 'code'}
          onChange={(event) => switchTo(event.target.value)}
        >
          <Radio value="code">{t('shell.studio.entity.currencyFixed')}</Radio>
          <Radio value="field">{t('shell.studio.entity.currencyFromField')}</Radio>
        </Radio.Group>
      </AxisField>
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
        <Typography.Text strong>{t('shell.studio.entity.indexes')}</Typography.Text>
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
                <AxisField {...fieldTexts(t, 'indexes.fields')}>
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
                <AxisField {...fieldTexts(t, 'indexes.unique')}>
                  <Checkbox
                    checked={content.unique === true}
                    onChange={(event) => onChange(`${pointer}/unique`, event.target.checked ? true : undefined)}
                  />
                </AxisField>
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
  )
}

/** The validations of the entity as rows of a name, an expression, a message key and a field. */
function Validations({
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
      <div data-testid="studio-field-/validations">
        <JsonOnly />
      </div>
    )
  }
  const list: unknown[] = validations ?? []

  return (
    <div data-testid="studio-field-/validations">
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.entity.validations')}</Typography.Text>
        {list.map((validation, index) => {
          const pointer = `/validations/${index}`
          const content = isContent(validation) ? validation : {}
          const expression = fieldTexts(t, 'validations.expression')
          return (
            <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${pointer}`}>
              <Typography.Text code data-testid={`studio-field-${pointer}/name`}>
                {text(content.name)}
              </Typography.Text>
              {layer && <LayerMarkTag layer={layer} list="/validations" index={index} name={text(content.name)} />}
              <div data-testid={`studio-field-${pointer}/expression`} style={{ flex: 1, minWidth: 240 }}>
                <AxisField {...expression}>
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
                // oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role.
                <Button danger onClick={() => onChange(pointer, undefined)}>
                  {t('shell.studio.entity.removeValidation')}
                </Button>
              )}
            </Flex>
          )
        })}
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
  )
}
