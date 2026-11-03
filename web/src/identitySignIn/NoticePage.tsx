import { Typography } from 'antd'
import { IdentityFrame } from './IdentityFrame'
import type { NoticeContext } from './signInContext'

/** A notice, such as "Your account has been updated": its message, and the link onward when Keycloak offers one. */
export function NoticePage({ context }: { context: NoticeContext }) {
  const { title, message, linkUrl, linkText } = context
  return (
    <IdentityFrame title={title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{message}</Typography.Paragraph>
      {linkUrl && <Typography.Link href={linkUrl}>{linkText ?? linkUrl}</Typography.Link>}
    </IdentityFrame>
  )
}
