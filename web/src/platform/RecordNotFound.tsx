import { Typography } from 'antd'
import { useContext } from 'react'
import { Link } from 'react-router'
import { RecordList } from './recordList'
import { useText } from './texts'

/**
 * Proposed, see the review on #3650: what a record's page shows under its heading when the record does not exist, or
 * the person cannot open it, as the server answers both alike. It says so in one line and keeps one link to the
 * record's list, the page it was opened from or else the site's first page, named by its navigation label. An address
 * that names no page still shows the not-found page.
 */
export function RecordNotFound() {
  const t = useText()
  const list = useContext(RecordList)
  // The line and its link read as one: what happened, then where to go.
  return (
    <Typography.Paragraph data-testid="record-not-found" style={{ margin: 0 }}>
      {t('shell.record.notFound')}
      {list && (
        <>
          {' '}
          <Link to={list.to}>{t('shell.record.goToList').replace('{page}', list.label)}</Link>
        </>
      )}
    </Typography.Paragraph>
  )
}
