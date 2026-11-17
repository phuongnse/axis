import { Tooltip } from 'antd'
import { formatValue } from './formatValue'
import { formatTimeAgo, isRecent, useMinuteClock } from './relativeTime'
import { useText } from './texts'
import { visuallyHidden } from './visuallyHidden'

/**
 * Proposed, see the reviews on #3263 and #3650: when something happened, for a record in time such as a history's
 * entry, a step's run or a notification: within a week how long ago it was in the locale's words, "5 minutes ago" or
 * "yesterday", moving on once a minute, with the exact date and time as its tooltip, which assistive technology reads
 * after the words; after a week, the date and time in the system's own format, as every other one shows.
 * A date or a time that is a record's data, such as a field or a due date, keeps its own format and never takes this.
 */
export function EventTime({ at, locale }: { at: string; locale: string }) {
  const t = useText()
  const now = useMinuteClock()
  const exact = formatValue({ name: 'at', type: 'date-time' }, at, {}, { locale, text: t }) as string
  if (!isRecent(at, now)) {
    return <time dateTime={at}>{exact}</time>
  }
  return (
    <Tooltip title={exact}>
      <time dateTime={at}>
        {formatTimeAgo(at, locale, now)}
        <span style={visuallyHidden}>{`, ${exact}`}</span>
      </time>
    </Tooltip>
  )
}
