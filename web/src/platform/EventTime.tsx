import { ConfigProvider, Tooltip } from 'antd'
import { useContext, useEffect, useState } from 'react'
import { formatTimeAgo, formatValue } from './formatValue'
import { useText } from './texts'
import { visuallyHidden } from './visuallyHidden'

const tick = 60_000

/**
 * When something happened: how long ago within a week, such as "5 minutes ago", and its date and time from a week on,
 * see docs/reference/frontend.md#shown-values. The exact date and time is the tooltip and is read after the words. The
 * words move on once a minute.
 */
export function EventTime({ value }: { value: string }) {
  const t = useText()
  // The shell sets the locale of Ant Design's ConfigProvider, as AxisTable reads it. Outside it, there is none.
  const locale = useContext(ConfigProvider.ConfigContext).locale?.locale ?? 'en'
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), tick)
    return () => clearInterval(timer)
  }, [])
  const options = { locale, text: t }
  const exact = formatValue({ name: 'time', type: 'date-time' }, value, {}, options)
  const words = formatTimeAgo(value, now, options)
  return (
    <Tooltip title={exact}>
      <time dateTime={value}>
        {words}
        {words !== exact && <span style={visuallyHidden}> {exact}</span>}
      </time>
    </Tooltip>
  )
}
