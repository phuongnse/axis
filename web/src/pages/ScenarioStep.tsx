import { Alert, Button, Card, Flex, Input, Select, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useContext, useState } from 'react'
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
import type { ChangeHandler } from './SchemaForm'
import { FieldCell } from './SeedGrid'
import { ReferencePicker } from './ReferencePicker'

const expectKinds = ['none', 'denied', 'notFound', 'conflict', 'invalid']

function startValue(field: StudioNameField): unknown {
  switch (field.type) {
    case 'enum':
      return field.values?.[0] ?? ''
    case 'boolean':
      return false
    case 'integer':
    case 'decimal':
      return 0
    default:
      return ''
  }
}

function toSeedField(field: StudioNameField): SeedField {
  return { name: field.name, type: field.type, values: field.values ?? undefined }
}

export function Field({ pointer, name, children }: { pointer: string; name: string; children: ReactNode }) {
  return (
    <div data-testid={`studio-field-${pointer}`}>
      <div>{name}</div>
      {children}
    </div>
  )
}

/** An input for a key of the file. The key changes when the input loses focus, and a key that is taken or empty is refused. */
export function KeyInput({
  name,
  value,
  onRename,
}: {
  name: string
  value: string
  onRename: (next: string) => boolean
}) {
  const [text, setText] = useState(value)
  return (
    <Input
      aria-label={name}
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
  keyLabel,
  addLabel,
  prefix,
  onChange,
}: {
  pointer: string
  values: Record<string, unknown>
  keyLabel: string
  addLabel: string
  prefix: string
  onChange: ChangeHandler
}) {
  const t = useText()
  return (
    <Flex vertical gap="small">
      {Object.keys(values).map((key) => (
        <Flex key={key} gap="small">
          <KeyInput
            name={keyLabel}
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
          <Input
            aria-label={key}
            value={literalText(values[key])}
            onChange={(event) => onChange(`${pointer}/${pointerSegment(key)}`, parseLiteral(event.target.value))}
          />
          <Button onClick={() => onChange(`${pointer}/${pointerSegment(key)}`, undefined)}>
            {t('shell.studio.scenario.remove')}
          </Button>
        </Flex>
      ))}
      <div>
        <Button onClick={() => onChange(`${pointer}/${freshKey(values, prefix)}`, '')}>{addLabel}</Button>
      </div>
    </Flex>
  )
}

/** The values of a step whose entity is not in the folder. The text is JSON and is saved only when it is an object. */
function JsonValues({ pointer, values, onChange }: { pointer: string; values: unknown; onChange: ChangeHandler }) {
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
      <Input.TextArea
        aria-label="values"
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
  const stepValue = steps[index]
  const step: Step = isObject(stepValue) ? stepValue : {}
  const type = stepTypes.find((candidate) => candidate === step.type)
  const pointer = `/steps/${index}`
  const at = (key: string) => `${pointer}/${key}`
  const text = (key: string) => (typeof step[key] === 'string' ? (step[key] as string) : undefined)
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

  function valuesControl(): ReactNode {
    const values = isObject(step.values) ? step.values : {}
    if (type === 'expectNotification') {
      return (
        <KeyValueRows
          pointer={at('values')}
          values={values}
          keyLabel={t('shell.studio.scenario.valueName')}
          addLabel={t('shell.studio.scenario.addValue')}
          prefix="value"
          onChange={onChange}
        />
      )
    }
    if (fields === undefined) {
      return <JsonValues key={`${index}-${type}`} pointer={pointer} values={step.values} onChange={onChange} />
    }
    const known = new Set(fields.map((field) => field.name))
    const missing = fields.filter((field) => !Object.hasOwn(values, field.name))
    return (
      <Flex vertical gap="small">
        {fields
          .filter((field) => Object.hasOwn(values, field.name))
          .map((field) => (
            <Field key={field.name} pointer={at(`values/${pointerSegment(field.name)}`)} name={field.name}>
              <FieldCell
                field={toSeedField(field)}
                value={values[field.name]}
                problem={undefined}
                onChange={(next) => onChange(at(`values/${pointerSegment(field.name)}`), next)}
              />
            </Field>
          ))}
        {Object.keys(values)
          .filter((key) => !known.has(key))
          .map((key) => (
            <Field key={key} pointer={at(`values/${pointerSegment(key)}`)} name={key}>
              <Input
                aria-label={key}
                value={literalText(values[key])}
                onChange={(event) => onChange(at(`values/${pointerSegment(key)}`), parseLiteral(event.target.value))}
              />
            </Field>
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
    )
  }

  function assigneeControl(): ReactNode {
    const assignee = isObject(step.assignee) ? step.assignee : undefined
    const kind = assignee?.role !== undefined ? 'role' : assignee?.user !== undefined ? 'user' : undefined
    const disabled = exclusive && step.state !== undefined
    return (
      <Flex gap="small">
        <Select
          virtual={false}
          aria-label="assignee kind"
          allowClear
          disabled={disabled}
          value={kind ?? null}
          style={{ minWidth: 120 }}
          options={[
            { value: 'user', label: t('shell.studio.scenario.assigneeUser') },
            { value: 'role', label: t('shell.studio.scenario.assigneeRole') },
          ]}
          onChange={(next: string | undefined) =>
            onChange(at('assignee'), next === undefined ? undefined : { [next]: '' })
          }
        />
        {kind !== undefined && (
          <ReferencePicker
            name={kind}
            value={typeof assignee?.[kind] === 'string' ? (assignee[kind] as string) : undefined}
            required
            options={kind === 'role' ? roles : users}
            onChange={(next) => onChange(at(`assignee/${kind}`), next ?? '')}
          />
        )}
      </Flex>
    )
  }

  function recipientControl(): ReactNode {
    const recipient = isObject(step.recipient) ? step.recipient : undefined
    const kind = (['role', 'user', 'email'] as const).find((key) => recipient?.[key] !== undefined)
    const recipientText =
      kind !== undefined && typeof recipient?.[kind] === 'string' ? (recipient[kind] as string) : undefined
    return (
      <Flex gap="small">
        <Select
          virtual={false}
          aria-label="recipient kind"
          value={kind ?? null}
          style={{ minWidth: 120 }}
          options={[
            { value: 'user', label: t('shell.studio.scenario.assigneeUser') },
            { value: 'role', label: t('shell.studio.scenario.assigneeRole') },
            { value: 'email', label: t('shell.studio.scenario.recipientEmail') },
          ]}
          onChange={(next: string) => onChange(at('recipient'), { [next]: '' })}
        />
        {kind === 'email' && (
          <Input
            aria-label="email"
            value={recipientText ?? ''}
            onChange={(event) => onChange(at('recipient/email'), event.target.value)}
          />
        )}
        {(kind === 'user' || kind === 'role') && (
          <ReferencePicker
            name={kind}
            value={recipientText}
            required
            options={kind === 'role' ? roles : users}
            onChange={(next) => onChange(at(`recipient/${kind}`), next ?? '')}
          />
        )}
      </Flex>
    )
  }

  function channelControl(): ReactNode {
    return (
      <Select
        virtual={false}
        aria-label="channel"
        value={text('channel') ?? 'none'}
        style={{ minWidth: 160 }}
        options={['none', 'inApp', 'email'].map((option) => ({ value: option, label: option }))}
        onChange={(next: string) => onChange(at('channel'), next === 'none' ? undefined : next)}
      />
    )
  }

  function expectControl(): ReactNode {
    const expect = step.expect
    const kind = typeof expect === 'string' ? expect : isObject(expect) ? 'invalid' : 'none'
    return (
      <Flex gap="small">
        <Select
          virtual={false}
          aria-label="expect"
          value={kind}
          disabled={step.save !== undefined}
          style={{ minWidth: 160 }}
          options={expectKinds.map((option) => ({ value: option, label: option }))}
          onChange={(next: string) =>
            onChange(at('expect'), next === 'none' ? undefined : next === 'invalid' ? { invalid: '' } : next)
          }
        />
        {kind === 'invalid' && (
          <Input
            aria-label="invalid"
            value={isObject(expect) && typeof expect.invalid === 'string' ? expect.invalid : ''}
            onChange={(event) => onChange(at('expect/invalid'), event.target.value)}
          />
        )}
      </Flex>
    )
  }

  function control(name: StepProperty): ReactNode {
    switch (name) {
      case 'entity':
        return (
          <ReferencePicker
            name={name}
            value={text(name)}
            required
            options={entities.map((entity) => entity.name)}
            onChange={(next) => onChange(at(name), next)}
          />
        )
      case 'record':
        return (
          <ReferencePicker
            name={name}
            value={text(name)}
            required
            options={recordAliases(steps, index)}
            onChange={(next) => onChange(at(name), next)}
          />
        )
      case 'save':
        return (
          <Input
            aria-label={name}
            value={text(name) ?? ''}
            disabled={step.expect !== undefined}
            onChange={(event) => onChange(at(name), event.target.value === '' ? undefined : event.target.value)}
          />
        )
      case 'as':
        return (
          <ReferencePicker
            name={name}
            value={text(name)}
            required
            options={users}
            onChange={(next) => onChange(at(name), next)}
          />
        )
      case 'process':
        return (
          <ReferencePicker
            name={name}
            value={text(name)}
            required
            options={processes.map((process) => process.name)}
            onChange={(next) => onChange(at(name), next)}
          />
        )
      case 'step':
        return (
          <ReferencePicker
            name={name}
            value={text(name)}
            required
            options={
              type === 'completeTask' || type === 'expectTask'
                ? taskStepOptions(processes, steps, index)
                : processStepOptions(processes, step.process)
            }
            onChange={(next) => onChange(at(name), next)}
          />
        )
      case 'outcome':
        return (
          <ReferencePicker
            name={name}
            value={text(name)}
            required
            options={outcomeOptions(processes, steps, index)}
            onChange={(next) => onChange(at(name), next)}
          />
        )
      case 'state': {
        const required = type === 'expectInstance'
        return (
          <Select
            virtual={false}
            aria-label={name}
            allowClear={!required}
            value={text(name) ?? null}
            disabled={exclusive && step.assignee !== undefined}
            style={{ minWidth: 160 }}
            options={(stateOptions[type!] ?? []).map((option) => ({ value: option, label: option }))}
            onChange={(next: string | undefined) => onChange(at(name), next)}
          />
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
          <Input
            aria-label={name}
            value={text(name) ?? ''}
            onChange={(event) => onChange(at(name), event.target.value === '' ? undefined : event.target.value)}
          />
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
            <Button disabled={index === 0} onClick={() => swap(index - 1)}>
              {t('shell.studio.scenario.moveUp')}
            </Button>
            <Button disabled={index === steps.length - 1} onClick={() => swap(index + 1)}>
              {t('shell.studio.scenario.moveDown')}
            </Button>
            <Button onClick={() => onChange(pointer, undefined)}>{t('shell.studio.scenario.remove')}</Button>
          </Flex>
        }
      >
        <Flex vertical gap="small">
          <Field pointer={at('type')} name={t('shell.studio.scenario.stepType')}>
            <Select
              showSearch
              optionFilterProp="label"
              virtual={false}
              aria-label={t('shell.studio.scenario.stepType')}
              value={type ?? null}
              style={{ minWidth: 200 }}
              options={stepTypes.map((option) => ({ value: option, label: option }))}
              onChange={(next: StepType) => onChange(pointer, changeStepType(step, next))}
            />
          </Field>
          {properties.map(({ name }) => (
            <Field key={name} pointer={at(name)} name={name}>
              {control(name)}
            </Field>
          ))}
          {type === undefined && <Typography.Text type="secondary">{t('shell.studio.scenario.none')}</Typography.Text>}
        </Flex>
      </Card>
    </div>
  )
}
