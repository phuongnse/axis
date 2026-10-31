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
  Table,
  Typography,
  DatePicker,
} from 'antd'
import dayjs from 'dayjs'
import { lazy, Suspense, useContext } from 'react'
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
import { LabelTextsContext } from './labelTextsContext'
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

/**
 * The designer of an entity. It shows the entity's name, label and display field, then its fields as a table of
 * name, label, type, required, unique and a settings column that shows only the settings the field's type takes,
 * and its validations as rows of an expression, a message key and a field. A field can be added, removed and moved,
 * and a type change drops the settings the new type does not take. A computed field's `expression` and each
 * validation's expression use the expression editor, and so does a field's `default`, which has no field in scope.
 * A field's settings also include `min` and `max` in the type of the field, `minLength` and `searchable`, and they
 * hide `default`, `min`, `max` and `minLength` next to an `expression`, a `sequence` or `creator`. An Indexes list
 * under the fields adds, moves and removes the entity's `indexes`, each a list of the entity's fields and a Unique
 * choice. The designer edits the draft through `onChange`, so a key it
 * does not show, such as `id`, `kind` or an unknown key on a field, stays as it is. Every input has the id
 * `studio-field-<pointer>`, and each row of the table has the id of its field, so a diagnostic opens at its input or
 * at the row of a setting that the row does not show. Clearing an optional value removes its key. With `readOnly`,
 * as for an entity of a module, every input is off, the label is its plain text key, and there are no buttons that
 * add, remove or move a field or a validation. With `origins`, the loaded name of each row of the fields table or
 * `null` for a row added in this session, Add field, Remove and Move report the origins that follow through
 * `onOriginsChange`, and Remove also reports the origin of the removed row as touched. With `warnings`, the row
 * numbers of fields whose change no migration follows, a warning shows in the settings column of that row.
 */
export function EntityDesigner({
  value,
  onChange,
  readOnly = false,
  origins,
  warnings,
  onOriginsChange,
}: {
  value: Content
  onChange: ChangeHandler
  readOnly?: boolean
  origins?: FieldOrigin[]
  warnings?: Map<number, FieldWarning>
  onOriginsChange?: (next: FieldOrigin[], touched?: string) => void
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const label = useContext(LabelTextsContext)
  const fields = Array.isArray(value.fields) ? value.fields : undefined
  const fieldNames = (fields ?? []).flatMap((field) =>
    isContent(field) && typeof field.name === 'string' && field.name !== '' ? [field.name] : [],
  )

  const designer = (
    <Flex vertical gap="middle" data-testid="studio-entity-designer">
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
      <Picker
        pointer="/displayField"
        label={t('shell.studio.entity.displayField')}
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
        onChange={onChange}
      />
      <Indexes indexes={value.indexes} fields={fields ?? []} readOnly={readOnly} onChange={onChange} />
      <Validations validations={value.validations} fieldNames={fieldNames} readOnly={readOnly} onChange={onChange} />
    </Flex>
  )

  return readOnly ? <ConfigProvider componentDisabled>{designer}</ConfigProvider> : designer
}

/** The fields of the entity as a table, with Add field below it. */
function Fields({
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

  return (
    <div data-testid="studio-field-/fields">
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.entity.fields')}</Typography.Text>
        <Table<unknown>
          size="small"
          pagination={false}
          scroll={{ x: 'max-content' }}
          dataSource={fields}
          rowKey={(row) => fields!.indexOf(row)}
          onRow={(_, index) =>
            ({ 'data-testid': `studio-field-/fields/${index}` }) as React.HTMLAttributes<HTMLElement>
          }
          columns={[
            {
              title: t('shell.studio.entity.fieldName'),
              render: (_, field, index) => (
                <div data-testid={`studio-field-/fields/${index}/name`}>
                  <Input
                    aria-label={t('shell.studio.entity.fieldName')}
                    value={text(isContent(field) ? field.name : undefined)}
                    onChange={(event) => onChange(`/fields/${index}/name`, event.target.value)}
                  />
                </div>
              ),
            },
            {
              title: t('shell.studio.entity.fieldLabel'),
              render: (_, field, index) => (
                <FieldLabel
                  name={t('shell.studio.entity.fieldLabel')}
                  pointer={`/fields/${index}/label`}
                  value={isContent(field) ? field.label : undefined}
                  onChange={onChange}
                />
              ),
            },
            {
              title: t('shell.studio.entity.fieldType'),
              render: (_, field, index) => {
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
              title: t('shell.studio.entity.required'),
              render: (_, field, index) => (
                <Flag field={field} setting="required" pointer={`/fields/${index}/required`} onChange={onChange} />
              ),
            },
            {
              title: t('shell.studio.entity.unique'),
              render: (_, field, index) => (
                <Flag field={field} setting="unique" pointer={`/fields/${index}/unique`} onChange={onChange} />
              ),
            },
            {
              title: t('shell.studio.entity.settings'),
              render: (_, field, index) => (
                <Flex vertical gap="small">
                  <FieldLabel
                    name={t('shell.studio.entity.help')}
                    pointer={`/fields/${index}/help`}
                    value={isContent(field) ? field.help : undefined}
                    onChange={onChange}
                  />
                  <Settings
                    index={index}
                    field={isContent(field) ? field : {}}
                    fields={fields}
                    readOnly={readOnly}
                    onChange={onChange}
                  />
                  {warnings?.get(index) && (
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
                    render: (_: unknown, __: unknown, index: number) => (
                      <Flex gap="small">
                        <Button disabled={index === 0} onClick={() => move(index, -1)}>
                          {t('shell.studio.entity.moveUp')}
                        </Button>
                        <Button disabled={index === fields.length - 1} onClick={() => move(index, 1)}>
                          {t('shell.studio.entity.moveDown')}
                        </Button>
                        <Button danger onClick={() => removeField(index)}>
                          {t('shell.studio.entity.removeField')}
                        </Button>
                      </Flex>
                    ),
                  },
                ]),
          ]}
        />
        {readOnly ? null : (
          <div>
            <Button onClick={addField}>{t('shell.studio.entity.addField')}</Button>
          </div>
        )}
      </Flex>
    </div>
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
 * The indexes of the entity as rows of a field picker and a Unique choice. The picker offers the fields that have a
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
        <Typography.Text strong>{t('shell.studio.entity.indexes')}</Typography.Text>
        {list.map((index, position) => {
          const pointer = `/indexes/${position}`
          const content = isContent(index) ? index : {}
          return (
            <Flex key={position} gap="small" align="flex-start" wrap data-testid={`studio-field-${pointer}`}>
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
            <Button onClick={() => write([...list, { fields: [] }])}>{t('shell.studio.entity.addIndex')}</Button>
          </div>
        )}
      </Flex>
    </div>
  )
}

/** The validations of the entity as rows of an expression, a message key and a field. */
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
        <Typography.Text strong>{t('shell.studio.entity.validations')}</Typography.Text>
        {list.map((validation, index) => {
          const pointer = `/validations/${index}`
          const content = isContent(validation) ? validation : {}
          const name = t('shell.studio.entity.validationExpression')
          return (
            <Flex key={index} gap="small" align="flex-start" wrap data-testid={`studio-field-${pointer}`}>
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
                onChange('/validations', [...list, { expression: '', message: { textKey: '' }, field: '' }])
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
