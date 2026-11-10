import { Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { IdentityFrame } from './IdentityFrame'
import type { ErrorContext } from './signInContext'

/** An error page: Keycloak's message as an error alert, with "Back to application" when the client has a home address. */
export function ErrorPage({ context }: { context: ErrorContext }) {
  const { title, error, backUrl, texts } = context
  return (
    <IdentityFrame title={title} frame={context}>
      <AxisAlert kind="error" message={error} />
      {backUrl && <Typography.Link href={backUrl}>{texts.backToApplication}</Typography.Link>}
    </IdentityFrame>
  )
}
