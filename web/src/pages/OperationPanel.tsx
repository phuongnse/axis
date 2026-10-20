import { Flex, Input, InputNumber, Select, Typography } from 'antd'
import { useContext, useState } from 'react'
import type { StudioNames } from '../platform/studio'
import { useText } from '../platform/texts'
import { ExpressionCheckContext } from './expressionCheckContext'
import { MappingRows } from './MappingRows'
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
 * The panel of an `operation` step: the operation to run and its properties. An operation is one of the three built
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
  const names = useContext(ExpressionCheckContext)?.names ?? null
  const operation = text(step.operation)
  const [packageMode, setPackageMode] = useState(false)
  const isPackage = !builtInOperations.includes(operation) && (packageMode || operation.includes('/'))
  const fields = names?.entities
    .find((item) => item.name.toLowerCase() === entity.toLowerCase())
    ?.fields.map((field) => field.name)

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
        <div>{t('shell.studio.process.operation')}</div>
        <Flex gap="small" wrap>
          <Select
            aria-label={t('shell.studio.process.operation')}
            style={{ minWidth: 240 }}
            virtual={false}
            value={isPackage ? packageChoice : operation === '' ? undefined : operation}
            options={[
              ...builtInOperations.map((option) => ({ value: option, label: option })),
              { value: packageChoice, label: t('shell.studio.process.packageOperation') },
            ]}
            onChange={choose}
          />
          {isPackage && (
            <Input
              aria-label={t('shell.studio.process.packageOperation')}
              placeholder="<package>/<operation>"
              style={{ width: 280 }}
              value={operation}
              onChange={(event) => onChange(`${pointer}/operation`, event.target.value)}
            />
          )}
        </Flex>
      </div>
      {operation === 'updateRecord' && (
        <MappingRows
          pointer={`${pointer}/set`}
          title={t('shell.studio.process.set')}
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
          <ConnectorMappings pointer={pointer} step={step} fields={fields} onChange={onChange} />
        </>
      )}
      {operation === 'raiseEvent' && <EventChoice pointer={pointer} step={step} names={names} onChange={onChange} />}
      {isPackage && <ConnectorMappings pointer={pointer} step={step} fields={fields} onChange={onChange} />}
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
  const connectorPointer = `${pointer}/connector`
  if (value !== undefined && !isContent(value)) {
    return (
      <div data-testid={`studio-field-${connectorPointer}`}>
        <div>{t('shell.studio.process.connector')}</div>
        <Typography.Text type="secondary" data-testid="studio-json-only">
          {t('shell.studio.editInJson')}
        </Typography.Text>
      </div>
    )
  }
  const connector = value ?? {}
  const name = text(connector.name)
  const operations =
    names?.connectors
      .find((item) => item.name.toLowerCase() === name.toLowerCase())
      ?.operations.map((operation) => operation.name) ?? (names === null ? undefined : [])

  const connectorLabel = t('shell.studio.process.connector')
  const operationLabel = t('shell.studio.process.connectorOperation')
  return (
    <Flex gap="small" wrap>
      <div data-testid={`studio-field-${connectorPointer}/name`}>
        <div>{connectorLabel}</div>
        {names === null ? (
          <Input
            aria-label={connectorLabel}
            value={name}
            onChange={(event) => onChange(`${connectorPointer}/name`, event.target.value)}
          />
        ) : (
          <ReferencePicker
            name={connectorLabel}
            value={name}
            required
            options={names.connectors.map((item) => item.name)}
            onChange={(next) => onChange(`${connectorPointer}/name`, next ?? '')}
          />
        )}
      </div>
      <div data-testid={`studio-field-${connectorPointer}/operation`}>
        <div>{operationLabel}</div>
        {operations === undefined ? (
          <Input
            aria-label={operationLabel}
            value={text(connector.operation)}
            onChange={(event) => onChange(`${connectorPointer}/operation`, event.target.value)}
          />
        ) : (
          <ReferencePicker
            name={operationLabel}
            value={text(connector.operation)}
            required
            options={operations}
            onChange={(next) => onChange(`${connectorPointer}/operation`, next ?? '')}
          />
        )}
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
  const eventPointer = `${pointer}/event`
  const event = text(step.event)
  const eventLabel = t('shell.studio.process.event')
  const payload = names?.events
    .find((item) => item.name.toLowerCase() === event.toLowerCase())
    ?.fields.map((field) => field.name)

  return (
    <>
      <div data-testid={`studio-field-${eventPointer}`}>
        <div>{eventLabel}</div>
        {names === null ? (
          <Input
            aria-label={eventLabel}
            value={event}
            onChange={(input) => onChange(eventPointer, input.target.value)}
          />
        ) : (
          <ReferencePicker
            name={eventLabel}
            value={event}
            required
            options={names.events.map((item) => item.name)}
            onChange={(next) => onChange(eventPointer, next ?? '')}
          />
        )}
      </div>
      <MappingRows
        pointer={`${pointer}/fields`}
        title={t('shell.studio.process.eventFields')}
        value={step.fields}
        keyLabel={t('shell.studio.process.eventField')}
        keyOptions={payload}
        target={{ kind: 'expression' }}
        onChange={onChange}
      />
    </>
  )
}

/** The `input`, `output` and `retry` that a connector call and a package operation share. */
function ConnectorMappings({
  pointer,
  step,
  fields,
  onChange,
}: {
  pointer: string
  step: Content
  fields: string[] | undefined
  onChange: ChangeHandler
}) {
  const t = useText()
  return (
    <>
      <MappingRows
        pointer={`${pointer}/input`}
        title={t('shell.studio.process.input')}
        value={step.input}
        keyLabel={t('shell.studio.process.inputName')}
        target={{ kind: 'expression' }}
        onChange={onChange}
      />
      <MappingRows
        pointer={`${pointer}/output`}
        title={t('shell.studio.process.output')}
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
  const retryPointer = `${pointer}/retry`
  if (value !== undefined && !isContent(value)) {
    return (
      <div data-testid={`studio-field-${retryPointer}`}>
        <div>{t('shell.studio.process.retry')}</div>
        <Typography.Text type="secondary" data-testid="studio-json-only">
          {t('shell.studio.editInJson')}
        </Typography.Text>
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
      <Flex vertical gap="small">
        <Typography.Text strong>{t('shell.studio.process.retry')}</Typography.Text>
        <Flex gap="small" wrap>
          <div>
            <div>{t('shell.studio.process.attempts')}</div>
            <InputNumber
              aria-label={t('shell.studio.process.attempts')}
              min={1}
              max={10}
              precision={0}
              value={typeof retry.attempts === 'number' ? retry.attempts : null}
              onChange={(next) => set('attempts', next ?? undefined)}
            />
          </div>
          <div>
            <div>{t('shell.studio.process.backoff')}</div>
            <Input
              aria-label={t('shell.studio.process.backoff')}
              value={text(retry.backoff)}
              onChange={(event) => set('backoff', event.target.value === '' ? undefined : event.target.value)}
            />
          </div>
        </Flex>
      </Flex>
    </div>
  )
}
