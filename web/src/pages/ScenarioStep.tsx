import { Alert, Button, Card, Flex, Input, Select, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useContext, useState } from 'react'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import type { StudioNameField } from '../platform/studio'
import {
  changeStepType,
  findNamed,
  freshKey,
  isObject,
  literalText,
  outcomeOptions,
  parseJsonText,
  parseLiteral,
  pointerSegment,
  processStepOptions,
  recordAliases,
  recordEntity,
  renameKey,
  stateOptions,
  stepProperties,
  stepTypes,
  taskStepOptions,
  type Step,
  type StepProperty,
  type StepType,
} from '../platform/scenarioSteps'
import type { SeedField } from '../platform/seedValues'
import { StudioNamesContext } from '../platform/studioNames'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useFieldTexts } from './fieldTexts'
import type { ChangeHandler } from './SchemaForm'
import { FieldCell } from './SeedGrid'
import { ReferencePicker } from './ReferencePicker'

const expectKinds = ['none', 'denied', 'notFound', 'conflict', 'invalid']

type Texts = { label: string; help: string }

function startValue(field: StudioNameField): unknown {
  switch (field.type) {
    case 'enum':
      return field.values?.[0] ?? ''
    case 'boolean':
      return false
    case 'integer':
    case 'decimal':
    case 'money':
      return 0
    default:
      return ''
  }
}

function toSeedField(field: StudioNameField): SeedField {
  return { name: field.name, type: field.type, values: field.values ?? undefined }
}

/** An input for a key of the file. The key changes when the input loses focus, and a key that is taken or empty is refused. */
export function KeyInput({
  value,
  onRename,
  id,
  'aria-describedby': describedBy,
}: {
  value: string
  onRename: (next: string) => boolean
  id?: string
  'aria-describedby'?: string
}) {
  const [text, setText] = useState(value)
  return (
    <Input
      id={id}
      aria-describedby={describedBy}
      value={text}
      onChange={(event) => setText(event.target.value)}
      onBlur={() => {
        if (text !== value && !onRename(text)) {
          setText(value)
        }
      }}
    />
  )
}

/** Rows of a key and a value, each removable, with a button that adds a row under a fresh key. */
export function KeyValueRows({
  pointer,
  values,
  group,
  keyField,
  addLabel,
  prefix,
  onChange,
}: {
  pointer: string
  values: Record<string, unknown>
  group: { title: string; help: string }
  keyField: Texts
  addLabel: string
  prefix: string
  onChange: ChangeHandler
}) {
  const t = useText()
  return (
    <AxisFieldGroup {...group}>
      <Flex vertical gap="small">
        {Object.keys(values).map((key) => (
          <Flex key={key} gap="small" align="flex-end">
            <AxisField {...keyField}>
              <KeyInput
                value={key}
                onRename={(next) => {
                  const renamed = renameKey(values, key, next)
                  if (renamed === undefined) {
                    return false
                  }
                  onChange(pointer, renamed)
                  return true
                }}
              />
            </AxisField>
            <Input
              aria-label={key}
              value={literalText(values[key])}
              onChange={(event) => onChange(`${pointer}/${pointerSegment(key)}`, parseLiteral(event.target.value))}
            />
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => onChange(`${pointer}/${pointerSegment(key)}`, undefined)}>
              {t('shell.studio.scenario.remove')}
            </Button>
          </Flex>
        ))}
        <div>
          {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
          <Button onClick={() => onChange(`${pointer}/${freshKey(values, prefix)}`, '')}>{addLabel}</Button>
        </div>
      </Flex>
    </AxisFieldGroup>
  )
}

/** The values of a step whose entity is not in the folder. The text is JSON and is saved only when it is an object. */
function JsonValues({
  pointer,
  values,
  texts,
  onChange,
}: {
  pointer: string
  values: unknown
  texts: Texts
  onChange: ChangeHandler
}) {
  const t = useText()
  const { type } = useDesignTokens()
  const formatted = JSON.stringify(values ?? {}, null, 2)
  const [text, setText] = useState(formatted)
  const [synced, setSynced] = useState(formatted)
  const [invalid, setInvalid] = useState(false)
  if (formatted !== synced) {
    setSynced(formatted)
    setText(formatted)
    setInvalid(false)
  }
  return (
    <Flex vertical gap="small">
      <AxisField {...texts}>
        <Input.TextArea
          autoSize={{ minRows: 3, maxRows: 12 }}
          spellCheck={false}
          style={{ fontFamily: type.codeFontFamily }}
          value={text}
          onChange={(event) => {
            setText(event.target.value)
            const parsed = parseJsonText(event.target.value)
            const ok = parsed.ok && isObject(parsed.value)
            setInvalid(!ok)
            if (parsed.ok && isObject(parsed.value)) {
              setSynced(JSON.stringify(parsed.value, null, 2))
              onChange(`${pointer}/values`, parsed.value)
            }
          }}
        />
      </AxisField>
      {/* oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert. */}
      {invalid && <Alert type="error" title={t('shell.studio.scenario.notJson')} />}
    </Flex>
  )
}

/**
 * One step of a scenario: its type and the properties that type takes. A value that the entity's fields do not name
 * stays as a text row, so nothing the file holds is hidden.
 */
export function ScenarioStep({
  steps,
  index,
  users,
  onChange,
}: {
  steps: unknown[]
  index: number
  users: string[]
  onChange: ChangeHandler
}) {
  const t = useText()
  const names = useContext(StudioNamesContext)
  const texts = useFieldTexts('scenario')
  const stepValue = steps[index]
  const step: Step = isObject(stepValue) ? stepValue : {}
  const type = stepTypes.find((candidate) => candidate === step.type)
  const pointer = `/steps/${index}`
  const at = (key: string) => `${pointer}/${key}`
  const text = (key: string) => (typeof step[key] === 'string' ? (step[key] as string) : undefined)
  const fieldText = (path: string) => texts.field(`steps.${path}`)
  const groupText = (path: string) => texts.group(`steps.${path}`)
  const entities = names?.entities ?? []
  const processes = names?.processes ?? []
  const roles = names?.roles.map((role) => role.name) ?? []
  const exclusive = type === 'expectTask'
  const fields = findNamed(entities, recordEntity(steps, index))?.fields.filter(
    (field) => field.type !== 'child-collection',
  )

  function swap(other: number) {
    const moved = [...steps]
    ;[moved[index], moved[other]] = [moved[other], moved[index]]
    onChange('/steps', moved)
  }

  function pickerField(name: StepProperty, options: string[]): ReactNode {
    const own = fieldText(name)
    return (
      <AxisField {...own}>
        <ReferencePicker
          name={own.label}
          value={text(name)}
          required
          options={options}
          onChange={(next) => onChange(at(name), next)}
        />
      </AxisField>
    )
  }

  function valuesControl(): ReactNode {
    const values = isObject(step.values) ? step.values : {}
    if (type === 'expectNotification') {
      return (
        <KeyValueRows
          pointer={at('values')}
          values={values}
          group={groupText('values')}
          keyField={fieldText('values.name')}
          addLabel={t('shell.studio.scenario.addValue')}
          prefix="value"
          onChange={onChange}
        />
      )
    }
    if (fields === undefined) {
      return (
        <JsonValues
          key={`${index}-${type}`}
          pointer={pointer}
          values={step.values}
          texts={fieldText('values')}
          onChange={onChange}
        />
      )
    }
    const known = new Set(fields.map((field) => field.name))
    const missing = fields.filter((field) => !Object.hasOwn(values, field.name))
    return (
      <AxisFieldGroup {...groupText('values')}>
        <Flex vertical gap="small">
          {fields
            .filter((field) => Object.hasOwn(values, field.name))
            .map((field) => (
              <div key={field.name} data-testid={`studio-field-${at(`values/${pointerSegment(field.name)}`)}`}>
                <div>{field.name}</div>
                <FieldCell
                  field={toSeedField(field)}
                  value={values[field.name]}
                  problem={undefined}
                  onChange={(next) => onChange(at(`values/${pointerSegment(field.name)}`), next)}
                />
              </div>
            ))}
          {Object.keys(values)
            .filter((key) => !known.has(key))
            .map((key) => (
              <div key={key} data-testid={`studio-field-${at(`values/${pointerSegment(key)}`)}`}>
                <div>{key}</div>
                <Input
                  aria-label={key}
                  value={literalText(values[key])}
                  onChange={(event) => onChange(at(`values/${pointerSegment(key)}`), parseLiteral(event.target.value))}
                />
              </div>
            ))}
          {missing.length > 0 && (
            <Select
              showSearch
              optionFilterProp="label"
              virtual={false}
              aria-label={t('shell.studio.scenario.addValue')}
              placeholder={t('shell.studio.scenario.addValue')}
              value={null}
              options={missing.map((field) => ({ value: field.name, label: field.name }))}
              onChange={(name: string) => {
                const field = missing.find((candidate) => candidate.name === name)!
                onChange(at(`values/${pointerSegment(name)}`), startValue(field))
              }}
            />
          )}
        </Flex>
      </AxisFieldGroup>
    )
  }

  function assigneeControl(): ReactNode {
    const assignee = isObject(step.assignee) ? step.assignee : undefined
    const kind = assignee?.role !== undefined ? 'role' : assignee?.user !== undefined ? 'user' : undefined
    const disabled = exclusive && step.state !== undefined
    return (
      <AxisFieldGroup {...groupText('assignee')}>
        <Flex vertical gap="small">
          <AxisField {...fieldText('assignee.kind')}>
            <Select
              virtual={false}
              allowClear
              disabled={disabled}
              value={kind ?? null}
              style={{ minWidth: 240 }}
              options={[
                { value: 'user', label: t('shell.studio.scenario.assigneeUser') },
                { value: 'role', label: t('shell.studio.scenario.assigneeRole') },
              ]}
              onChange={(next: string | undefined) =>
                onChange(at('assignee'), next === undefined ? undefined : { [next]: '' })
              }
            />
          </AxisField>
          {kind !== undefined && (
            <AxisField {...fieldText(`assignee.${kind}`)}>
              <ReferencePicker
                name={fieldText(`assignee.${kind}`).label}
                value={typeof assignee?.[kind] === 'string' ? (assignee[kind] as string) : undefined}
                required
                options={kind === 'role' ? roles : users}
                onChange={(next) => onChange(at(`assignee/${kind}`), next ?? '')}
              />
            </AxisField>
          )}
        </Flex>
      </AxisFieldGroup>
    )
  }

  function recipientControl(): ReactNode {
    const recipient = isObject(step.recipient) ? step.recipient : undefined
    const kind = (['role', 'user', 'email'] as const).find((key) => recipient?.[key] !== undefined)
    const recipientText =
      kind !== undefined && typeof recipient?.[kind] === 'string' ? (recipient[kind] as string) : undefined
    return (
      <AxisFieldGroup {...groupText('recipient')}>
        <Flex vertical gap="small">
          <AxisField {...fieldText('recipient.kind')}>
            <Select
              virtual={false}
              value={kind ?? null}
              style={{ minWidth: 240 }}
              options={[
                { value: 'user', label: t('shell.studio.scenario.assigneeUser') },
                { value: 'role', label: t('shell.studio.scenario.assigneeRole') },
                { value: 'email', label: t('shell.studio.scenario.recipientEmail') },
              ]}
              onChange={(next: string) => onChange(at('recipient'), { [next]: '' })}
            />
          </AxisField>
          {kind === 'email' && (
            <AxisField {...fieldText('recipient.email')}>
              <Input
                value={recipientText ?? ''}
                onChange={(event) => onChange(at('recipient/email'), event.target.value)}
              />
            </AxisField>
          )}
          {(kind === 'user' || kind === 'role') && (
            <AxisField {...fieldText(`recipient.${kind}`)}>
              <ReferencePicker
                name={fieldText(`recipient.${kind}`).label}
                value={recipientText}
                required
                options={kind === 'role' ? roles : users}
                onChange={(next) => onChange(at(`recipient/${kind}`), next ?? '')}
              />
            </AxisField>
          )}
        </Flex>
      </AxisFieldGroup>
    )
  }

  function channelControl(): ReactNode {
    return (
      <AxisField {...fieldText('channel')}>
        <Select
          virtual={false}
          value={text('channel') ?? 'none'}
          style={{ minWidth: 160 }}
          options={['none', 'inApp', 'email'].map((option) => ({ value: option, label: option }))}
          onChange={(next: string) => onChange(at('channel'), next === 'none' ? undefined : next)}
        />
      </AxisField>
    )
  }

  function expectControl(): ReactNode {
    const expect = step.expect
    const kind = typeof expect === 'string' ? expect : isObject(expect) ? 'invalid' : 'none'
    return (
      <Flex vertical gap="small">
        <AxisField {...fieldText('expect')}>
          <Select
            virtual={false}
            value={kind}
            disabled={step.save !== undefined}
            style={{ minWidth: 160 }}
            options={expectKinds.map((option) => ({ value: option, label: option }))}
            onChange={(next: string) =>
              onChange(at('expect'), next === 'none' ? undefined : next === 'invalid' ? { invalid: '' } : next)
            }
          />
        </AxisField>
        {kind === 'invalid' && (
          <AxisField {...fieldText('expect.invalid')}>
            <Input
              value={isObject(expect) && typeof expect.invalid === 'string' ? expect.invalid : ''}
              onChange={(event) => onChange(at('expect/invalid'), event.target.value)}
            />
          </AxisField>
        )}
      </Flex>
    )
  }

  function control(name: StepProperty): ReactNode {
    switch (name) {
      case 'entity':
        return pickerField(
          name,
          entities.map((entity) => entity.name),
        )
      case 'record':
        return pickerField(name, recordAliases(steps, index))
      case 'save':
        return (
          <AxisField {...fieldText(name)}>
            <Input
              value={text(name) ?? ''}
              disabled={step.expect !== undefined}
              onChange={(event) => onChange(at(name), event.target.value === '' ? undefined : event.target.value)}
            />
          </AxisField>
        )
      case 'as':
        return pickerField(name, users)
      case 'process':
        return pickerField(
          name,
          processes.map((process) => process.name),
        )
      case 'step':
        return pickerField(
          name,
          type === 'completeTask' || type === 'expectTask'
            ? taskStepOptions(processes, steps, index)
            : processStepOptions(processes, step.process),
        )
      case 'outcome':
        return pickerField(name, outcomeOptions(processes, steps, index))
      case 'state': {
        const required = type === 'expectInstance'
        return (
          <AxisField {...fieldText(name)}>
            <Select
              virtual={false}
              allowClear={!required}
              value={text(name) ?? null}
              disabled={exclusive && step.assignee !== undefined}
              style={{ minWidth: 160 }}
              options={(stateOptions[type!] ?? []).map((option) => ({ value: option, label: option }))}
              onChange={(next: string | undefined) => onChange(at(name), next)}
            />
          </AxisField>
        )
      }
      case 'assignee':
        return assigneeControl()
      case 'values':
        return valuesControl()
      case 'expect':
        return expectControl()
      case 'recipient':
        return recipientControl()
      case 'channel':
        return channelControl()
      case 'by':
        return (
          <AxisField {...fieldText(name)}>
            <Input
              value={text(name) ?? ''}
              onChange={(event) => onChange(at(name), event.target.value === '' ? undefined : event.target.value)}
            />
          </AxisField>
        )
    }
  }

  const properties = type ? stepProperties[type] : []
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <Card
        size="small"
        data-testid="studio-scenario-step"
        title={`${t('shell.studio.scenario.step')} ${index + 1}`}
        extra={
          <Flex gap="small">
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button disabled={index === 0} onClick={() => swap(index - 1)}>
              {t('shell.studio.scenario.moveUp')}
            </Button>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button disabled={index === steps.length - 1} onClick={() => swap(index + 1)}>
              {t('shell.studio.scenario.moveDown')}
            </Button>
            {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
            <Button onClick={() => onChange(pointer, undefined)}>{t('shell.studio.scenario.remove')}</Button>
          </Flex>
        }
      >
        <Flex vertical gap="small">
          <div data-testid={`studio-field-${at('type')}`}>
            <AxisField {...fieldText('type')}>
              <Select
                showSearch
                optionFilterProp="label"
                virtual={false}
                value={type ?? null}
                style={{ minWidth: 200 }}
                options={stepTypes.map((option) => ({ value: option, label: option }))}
                onChange={(next: StepType) => onChange(pointer, changeStepType(step, next))}
              />
            </AxisField>
          </div>
          {properties.map(({ name }) => (
            <div key={name} data-testid={`studio-field-${at(name)}`}>
              {control(name)}
            </div>
          ))}
          {type === undefined && <Typography.Text type="secondary">{t('shell.studio.scenario.none')}</Typography.Text>}
        </Flex>
      </Card>
    </div>
  )
}
