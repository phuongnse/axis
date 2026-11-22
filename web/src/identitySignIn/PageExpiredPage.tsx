import { Typography } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import { IdentityFrame } from './IdentityFrame'
import type { PageExpiredContext } from './signInContext'

/** The expired page: what happened, Continue to the login address and Start again at the restart address. */
export function PageExpiredPage({ context }: { context: PageExpiredContext }) {
  const { title, texts, continueUrl, restartUrl } = context
  return (
    <IdentityFrame title={title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      <AxisButton role="finish" href={continueUrl} block>
        {texts.continue}
      </AxisButton>
      <Typography.Link href={restartUrl}>{texts.startAgain}</Typography.Link>
    </IdentityFrame>
  )
}
