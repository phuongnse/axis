import { Input, Typography } from 'antd'
import { useText } from '../platform/texts'
import type { StudioNames } from '../platform/studio'
import { MappingRows } from './MappingRows'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

/**
 * The `event` of an endpoint: the event it raises and the `fields` that give each payload field of the event its value.
 * The picker clears the whole `event` when it is empty, and a picked event keeps the rows of `fields`. A value that is
 * not an object is left to the JSON view.
 */
export function EndpointEvent({
  events,
  value,
  onChange,
}: {
  events: StudioNames['events'] | undefined
  value: unknown
  onChange: ChangeHandler
}) {
  const t = useText()
  const eventLabel = t('shell.studio.process.event')

  if (value !== undefined && (typeof value !== 'object' || value === null || Array.isArray(value))) {
    return (
      <div data-testid="studio-field-/event">
        <div>{eventLabel}</div>
        <Typography.Text type="secondary" data-testid="studio-json-only">
          {t('shell.studio.editInJson')}
        </Typography.Text>
      </div>
    )
  }

  const entry = value as { name?: unknown; fields?: unknown } | undefined
  const name = typeof entry?.name === 'string' ? entry.name : ''
  const payload = events
    ?.find((item) => item.name.toLowerCase() === name.toLowerCase())
    ?.fields.map((field) => field.name)

  function pick(next: string | undefined) {
    if (!next) {
      onChange('/event', undefined)
    } else if (entry === undefined) {
      onChange('/event', { name: next, fields: {} })
    } else {
      onChange('/event/name', next)
    }
  }

  return (
    <>
      <div data-testid="studio-field-/event/name">
        <div>{eventLabel}</div>
        {events === undefined ? (
          <Input aria-label={eventLabel} value={name} onChange={(input) => pick(input.target.value)} />
        ) : (
          <ReferencePicker
            name={eventLabel}
            value={name}
            required={false}
            options={events.map((item) => item.name)}
            onChange={pick}
          />
        )}
      </div>
      {entry !== undefined && (
        <MappingRows
          pointer="/event/fields"
          title={t('shell.studio.process.eventFields')}
          value={entry.fields}
          keyLabel={t('shell.studio.process.eventField')}
          keyOptions={payload}
          target={{ kind: 'expression' }}
          onChange={onChange}
        />
      )}
    </>
  )
}
