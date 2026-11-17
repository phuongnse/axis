import { Tooltip } from 'antd'
import { formatValue } from './formatValue'
import { formatEventTime, useMinuteClock } from './relativeTime'
import { useText } from './texts'
import { visuallyHidden } from './visuallyHidden'

/**
 * Proposed, see the reviews on #3263 and #3650: when something happened, for a record in time such as a history's
 * entry, a step's run or a notification: within a week how long ago it was in the locale's words, "5 minutes ago" or
 * "yesterday", moving on once a minute, and after that its date, "4 Mar". The exact date and time is its tooltip, and
 * assistive technology reads it after the words.
 * A date or a time that is a record's data, such as a field or a due date, keeps its own format and never takes this.
 */
export function EventTime({ at, locale }: { at: string; locale: string }) {
  const t = useText()
  const now = useMinuteClock()
  const exact = formatValue({ name: 'at', type: 'date-time' }, at, {}, { locale, text: t }) as string
  return (
    <Tooltip title={exact}>
      <time dateTime={at}>
        {formatEventTime(at, locale, now)}
        <span style={visuallyHidden}>{`, ${exact}`}</span>
      </time>
    </Tooltip>
  )
}
