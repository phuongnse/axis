import { Input, Typography } from 'antd'
import { SectionPart } from '../platform/SectionParts'
import { useText } from '../platform/texts'
import type { StudioNames } from '../platform/studio'
import { useFieldTexts } from './fieldTexts'
import { MappingRows } from './MappingRows'
import { ReferencePicker } from './ReferencePicker'
import type { ChangeHandler } from './SchemaForm'

/**
 * The `event` of an endpoint: the event it raises and the `fields` that give each payload field of the event its value.
 * The picker clears the whole `event` when it is empty, and a picked event keeps the rows of `fields`. The part is
 * headed with the event's title and help. A value that is not an object is left to the JSON view.
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
  const { group } = useFieldTexts('endpoint')
  const eventLabel = t('shell.studio.process.event')
  const heading = { title: t('shell.studio.field.endpoint.event'), line: t('shell.studio.field.endpoint.event.help') }

  if (value !== undefined && (typeof value !== 'object' || value === null || Array.isArray(value))) {
    return (
      <SectionPart testName="event" heading={heading}>
        <div data-testid="studio-field-/event">
          <Typography.Text type="secondary" data-testid="studio-json-only">
            {t('shell.studio.editInJson')}
          </Typography.Text>
        </div>
      </SectionPart>
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
    <SectionPart testName="event" heading={heading}>
      <div data-testid="studio-field-/event/name">
        <div>{eventLabel}</div>
        {events === undefined ? (
          <Input aria-label={eventLabel} value={name} onChange={(input) => pick(input.target.value)} />
        ) : (
          <ReferencePicker name={eventLabel} value={name} required={false} options={events} onChange={pick} />
        )}
      </div>
      {entry !== undefined && (
        <MappingRows
          pointer="/event/fields"
          {...group('event.fields')}
          value={entry.fields}
          keyLabel={t('shell.studio.process.eventField')}
          keyOptions={payload}
          target={{ kind: 'expression' }}
          onChange={onChange}
        />
      )}
    </SectionPart>
  )
}
