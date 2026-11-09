import { Flex, Input, InputNumber, Select, Typography } from 'antd'
import { useContext, useState } from 'react'
import { AxisField } from '../platform/AxisField'
import { AxisFieldGroup } from '../platform/AxisFieldGroup'
import type { StudioNames } from '../platform/studio'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { ConnectorInputRows } from './ConnectorInputRows'
import { useFieldTexts } from './fieldTexts'
import { MappingRows } from './MappingRows'
import { NotifyFields } from './NotifyFields'
import { builtInOperations, forbiddenByOperation, packageChoice, withOperation, without } from './operationKeys'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

type Content = Record<string, unknown>

function isContent(value: unknown): value is Content {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

/**
 * The panel of an `operation` step: the operation to run and its properties. An operation is one of the five built
 * in, or a package's operation that is typed as `<package>/<operation>`. A key the operation forbids is not shown,
 * and the step keeps it until the author removes it in the JSON view.
 */
export function OperationPanel({
  pointer,
  step,
  entity,
  onChange,
}: {
  pointer: string
  step: Content
  entity: string
  onChange: ChangeHandler
}) {
  const t = useText()
  const { field, group } = useFieldTexts('process')
  const names = useContext(ExpressionCheckContext)?.names ?? null
  const operation = text(step.operation)
  const [packageMode, setPackageMode] = useState(false)
  const isPackage = !builtInOperations.includes(operation) && (packageMode || operation.includes('/'))
  const fields = names?.entities
    .find((item) => item.name.toLowerCase() === entity.toLowerCase())
    ?.fields.map((item) => item.name)

  function choose(next: string) {
    if (next === packageChoice) {
      setPackageMode(true)
      onChange(pointer, { ...without(step, forbiddenByOperation[packageChoice]), operation: '' })
      return
    }
    setPackageMode(false)
    onChange(pointer, withOperation(step, next))
  }

  return (
    <>
      <div data-testid={`studio-field-${pointer}/operation`}>
        <Flex gap="small" wrap>
          <AxisField {...field('steps.operation')}>
            <Select
              style={{ minWidth: 240 }}
              virtual={false}
              value={isPackage ? packageChoice : operation === '' ? undefined : operation}
              options={[
                ...builtInOperations.map((option) => ({ value: option, label: option })),
                { value: packageChoice, label: t('shell.studio.process.packageOperation') },
              ]}
              onChange={choose}
            />
          </AxisField>
          {isPackage && (
            <AxisField {...field('steps.packageOperation')}>
              <Input
                style={{ width: 280 }}
                value={operation}
                onChange={(event) => onChange(`${pointer}/operation`, event.target.value)}
              />
            </AxisField>
          )}
        </Flex>
      </div>
      {operation === 'updateRecord' && (
        <MappingRows
          pointer={`${pointer}/set`}
          {...group('steps.set')}
          value={step.set}
          keyLabel={t('shell.studio.process.field')}
          keyOptions={fields}
          target={{ kind: 'expression' }}
          onChange={onChange}
        />
      )}
      {operation === 'callConnector' && (
        <>
          <ConnectorChoice pointer={pointer} value={step.connector} names={names} onChange={onChange} />
          <ConnectorMappings pointer={pointer} step={step} fields={fields} entity={entity} nested onChange={onChange} />
        </>
      )}
      {operation === 'raiseEvent' && <EventChoice pointer={pointer} step={step} names={names} onChange={onChange} />}
      {operation === 'createRecord' && (
        <CreateEntityChoice pointer={pointer} step={step} names={names} onChange={onChange} />
      )}
      {operation === 'notify' && <NotifyFields pointer={pointer} path="steps" value={step} onChange={onChange} />}
      {isPackage && (
        <ConnectorMappings
          pointer={pointer}
          step={step}
          fields={fields}
          entity={entity}
          nested={false}
          onChange={onChange}
        />
      )}
    </>
  )
}

/** The connector of a `callConnector` step and one of its operations. Both are typed while the names are unknown. */
function ConnectorChoice({
  pointer,
  value,
  names,
  onChange,
}: {
  pointer: string
  value: unknown
  names: StudioNames | null
  onChange: ChangeHandler
}) {
  const t = useText()
  const { field, group } = useFieldTexts('process')
  const connectorPointer = `${pointer}/connector`
  if (value !== undefined && !isContent(value)) {
    return (
      <div data-testid={`studio-field-${connectorPointer}`}>
        <AxisFieldGroup {...group('steps.connector')}>
          <Typography.Text type="secondary" data-testid="studio-json-only">
            {t('shell.studio.editInJson')}
          </Typography.Text>
        </AxisFieldGroup>
      </div>
    )
  }
  const connector = value ?? {}
  const name = text(connector.name)
  const operations =
    names?.connectors
      .find((item) => item.name.toLowerCase() === name.toLowerCase())
      ?.operations.map((operation) => operation.name) ?? (names === null ? undefined : [])

  const nameField = field('steps.connector.name')
  const operationField = field('steps.connector.operation')
  return (
    <Flex vertical gap="small">
      <div data-testid={`studio-field-${connectorPointer}/name`}>
        <AxisField {...nameField}>
          {names === null ? (
            <Input value={name} onChange={(event) => onChange(`${connectorPointer}/name`, event.target.value)} />
          ) : (
            <ReferencePicker
              name={nameField.label}
              value={name}
              required
              options={names.connectors.map((item) => item.name)}
              onChange={(next) => onChange(`${connectorPointer}/name`, next ?? '')}
            />
          )}
        </AxisField>
      </div>
      <div data-testid={`studio-field-${connectorPointer}/operation`}>
        <AxisField {...operationField}>
          {operations === undefined ? (
            <Input
              value={text(connector.operation)}
              onChange={(event) => onChange(`${connectorPointer}/operation`, event.target.value)}
            />
          ) : (
            <ReferencePicker
              name={operationField.label}
              value={text(connector.operation)}
              required
              options={operations}
              onChange={(next) => onChange(`${connectorPointer}/operation`, next ?? '')}
            />
          )}
        </AxisField>
      </div>
    </Flex>
  )
}

/** The event of a `raiseEvent` step and its `fields`, which are keyed by the event's payload fields. */
function EventChoice({
  pointer,
  step,
  names,
  onChange,
}: {
  pointer: string
  step: Content
  names: StudioNames | null
  onChange: ChangeHandler
}) {
  const t = useText()
  const { field, group } = useFieldTexts('process')
  const eventPointer = `${pointer}/event`
  const event = text(step.event)
  const eventField = field('steps.event')
  const payload = names?.events
    .find((item) => item.name.toLowerCase() === event.toLowerCase())
    ?.fields.map((item) => item.name)

  return (
    <>
      <div data-testid={`studio-field-${eventPointer}`}>
        <AxisField {...eventField}>
          {names === null ? (
            <Input value={event} onChange={(input) => onChange(eventPointer, input.target.value)} />
          ) : (
            <ReferencePicker
              name={eventField.label}
              value={event}
              required
              options={names.events.map((item) => item.name)}
              onChange={(next) => onChange(eventPointer, next ?? '')}
            />
          )}
        </AxisField>
      </div>
      <MappingRows
        pointer={`${pointer}/fields`}
        {...group('steps.fields')}
        value={step.fields}
        keyLabel={t('shell.studio.process.eventField')}
        keyOptions={payload}
        target={{ kind: 'expression' }}
        onChange={onChange}
      />
    </>
  )
}

/** The entity a `createRecord` step creates a record of and its `set`, which is keyed by that entity's fields. */
function CreateEntityChoice({
  pointer,
  step,
  names,
  onChange,
}: {
  pointer: string
  step: Content
  names: StudioNames | null
  onChange: ChangeHandler
}) {
  const t = useText()
  const { field, group } = useFieldTexts('process')
  const entityPointer = `${pointer}/entity`
  const entity = text(step.entity)
  const entityField = field('steps.entity')
  const fields = names?.entities
    .find((item) => item.name.toLowerCase() === entity.toLowerCase())
    ?.fields.map((item) => item.name)

  return (
    <>
      <div data-testid={`studio-field-${entityPointer}`}>
        <AxisField {...entityField}>
          {names === null ? (
            <Input value={entity} onChange={(input) => onChange(entityPointer, input.target.value)} />
          ) : (
            <ReferencePicker
              name={entityField.label}
              value={entity}
              required
              options={names.entities.map((item) => item.name)}
              onChange={(next) => onChange(entityPointer, next ?? '')}
            />
          )}
        </AxisField>
      </div>
      <MappingRows
        pointer={`${pointer}/set`}
        {...group('steps.set')}
        value={step.set}
        keyLabel={t('shell.studio.process.field')}
        keyOptions={fields}
        target={{ kind: 'expression' }}
        onChange={onChange}
      />
    </>
  )
}

/**
 * The `input`, `output` and `retry` that a connector call and a package operation share. A connector call's `input`
 * can nest, as an `object` or a `list` of the subject's child collection, and a package operation's stays scalar.
 */
function ConnectorMappings({
  pointer,
  step,
  fields,
  entity,
  nested,
  onChange,
}: {
  pointer: string
  step: Content
  fields: string[] | undefined
  entity: string
  nested: boolean
  onChange: ChangeHandler
}) {
  const t = useText()
  const { group } = useFieldTexts('process')
  return (
    <>
      {nested ? (
        <ConnectorInputRows
          pointer={`${pointer}/input`}
          {...group('steps.input')}
          keyLabel={t('shell.studio.process.inputName')}
          value={step.input}
          entity={entity}
          onChange={onChange}
        />
      ) : (
        <MappingRows
          pointer={`${pointer}/input`}
          {...group('steps.input')}
          value={step.input}
          keyLabel={t('shell.studio.process.inputName')}
          target={{ kind: 'expression' }}
          onChange={onChange}
        />
      )}
      <MappingRows
        pointer={`${pointer}/output`}
        {...group('steps.output')}
        value={step.output}
        keyLabel={t('shell.studio.process.connectorOutput')}
        target={{ kind: 'field', label: t('shell.studio.process.entityField'), options: fields }}
        onChange={onChange}
      />
      <Retry pointer={pointer} value={step.retry} onChange={onChange} />
    </>
  )
}

/** The retry policy of a step. An empty value removes its key, and the last key removed takes `retry` with it. */
function Retry({ pointer, value, onChange }: { pointer: string; value: unknown; onChange: ChangeHandler }) {
  const t = useText()
  const { field, group } = useFieldTexts('process')
  const retryPointer = `${pointer}/retry`
  if (value !== undefined && !isContent(value)) {
    return (
      <div data-testid={`studio-field-${retryPointer}`}>
        <AxisFieldGroup {...group('steps.retry')}>
          <Typography.Text type="secondary" data-testid="studio-json-only">
            {t('shell.studio.editInJson')}
          </Typography.Text>
        </AxisFieldGroup>
      </div>
    )
  }
  const retry = value ?? {}

  function set(key: string, next: unknown) {
    if (next !== undefined) {
      onChange(`${retryPointer}/${key}`, next)
    } else if (Object.keys(retry).every((other) => other === key)) {
      onChange(retryPointer, undefined)
    } else {
      onChange(`${retryPointer}/${key}`, undefined)
    }
  }

  return (
    <div data-testid={`studio-field-${retryPointer}`}>
      <AxisFieldGroup {...group('steps.retry')}>
        <Flex gap="small" wrap>
          <AxisField {...field('steps.retry.attempts')}>
            <InputNumber
              min={1}
              max={10}
              precision={0}
              value={typeof retry.attempts === 'number' ? retry.attempts : null}
              onChange={(next) => set('attempts', next ?? undefined)}
            />
          </AxisField>
          <AxisField {...field('steps.retry.backoff')}>
            <Input
              value={text(retry.backoff)}
              onChange={(event) => set('backoff', event.target.value === '' ? undefined : event.target.value)}
            />
          </AxisField>
        </Flex>
      </AxisFieldGroup>
    </div>
  )
}
