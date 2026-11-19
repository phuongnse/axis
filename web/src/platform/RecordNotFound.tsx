import { Typography } from 'antd'
import { useContext } from 'react'
import { Link } from 'react-router'
import { fillText } from './fillText'
import { RecordList } from './recordList'
import { useText } from './texts'

/**
 * The line a page shows under its heading for a record that does not exist or that the person cannot open: one line
 * of body text and one link back to its list, with no illustration. Without a `RecordList` it shows the line only.
 * See docs/reference/frontend.md#routing.
 */
export function RecordNotFound({ textKey = 'shell.record.notFound' }: { textKey?: string }) {
  const t = useText()
  const list = useContext(RecordList)
  return (
    <Typography.Paragraph data-testid="record-not-found">
      {t(textKey)}
      {list && (
        <>
          {' '}
          <Link to={list.to}>{fillText(t('shell.record.goToList'), { page: t(list.labelKey) })}</Link>
        </>
      )}
    </Typography.Paragraph>
  )
}
