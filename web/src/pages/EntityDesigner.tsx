import { Alert, Button, Checkbox, ConfigProvider, Flex, Input, InputNumber, Select, Table, Typography } from 'antd'
import { lazy, Suspense, useContext } from 'react'
import { changeFieldType, fieldSettings, fieldTypes, type FieldSetting } from '../platform/entityFields'
import type { FieldOrigin, FieldWarning } from '../platform/entityMigrations'
import { referenceOptions, StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { expressionScope } from './expressionScope'
import { LabelTexts } from './LabelTexts'
import { LabelTextsContext } from './labelTextsContext'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

const LazyExpressionEditor = lazy(() => import('./ExpressionEditor'))

type Content = Record<string, unknown>

/** The settings that the settings column shows, in the order it shows them. `required` and `unique` have columns. */
const settingColumns: FieldSetting[] = [
  'maxLength',
  'precision',
  'scale',
  'target',
  'values',
  'expression',
  'sequence',
  'creator',
  'sensitive',
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
 * validation's expression use the expression editor. The designer edits the draft through `onChange`, so a key it
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
          rowKey={(_, index) => index!}
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
                  <Settings
                    index={index}
                    field={isContent(field) ? field : {}}
                    readOnly={readOnly}
                    onChange={onChange}
                  />
                  {warnings?.get(index) && (
                    <Alert
                      type="warning"
                      data-testid={`studio-migration-warning-/fields/${index}`}
                      message={t(
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

/** The text key picker of a field's label. Clearing it removes the label. */
function FieldLabel({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const name = t('shell.studio.entity.fieldLabel')
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
  readOnly,
  onChange,
}: {
  index: number
  field: Content
  readOnly: boolean
  onChange: ChangeHandler
}) {
  const t = useText()
  const pointer = `/fields/${index}`
  const taken = fieldSettings(text(field.type))
  const shown = settingColumns.filter((setting) => taken.includes(setting))

  const labelOf = (setting: FieldSetting) => t(`shell.studio.entity.${setting}`)

  function number(setting: 'maxLength' | 'precision' | 'scale') {
    return (
      <InputNumber
        aria-label={labelOf(setting)}
        precision={0}
        min={0}
        value={typeof field[setting] === 'number' ? field[setting] : null}
        onChange={(next) => onChange(`${pointer}/${setting}`, typeof next === 'number' ? next : undefined)}
      />
    )
  }

  function flag(setting: 'creator' | 'sensitive') {
    return (
      <Checkbox
        aria-label={labelOf(setting)}
        checked={field[setting] === true}
        onChange={(event) => onChange(`${pointer}/${setting}`, event.target.checked)}
      />
    )
  }

  function input(setting: FieldSetting) {
    switch (setting) {
      case 'maxLength':
      case 'precision':
      case 'scale':
        return number(setting)
      case 'creator':
      case 'sensitive':
        return flag(setting)
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
        setting === 'target' || setting === 'sequence' ? (
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
