import { Button, Typography } from 'antd'
import { IdentityFrame } from './IdentityFrame'
import type { SignOutContext } from './signInContext'

/**
 * The sign-out question, which Keycloak shows only when the server could not end the session itself. Sign out is a
 * real `<form method="post">` to `action` that posts the session code as `session_code`, with `confirmLogout=on`.
 * "Back to application" shows when Keycloak gives an address.
 */
export function SignOutPage({ context }: { context: SignOutContext }) {
  const { title, texts, action, sessionCode, backUrl } = context
  return (
    <IdentityFrame title={title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      <form method="post" action={action}>
        <input type="hidden" name="session_code" value={sessionCode} />
        <input type="hidden" name="confirmLogout" value="on" />
        <Button type="primary" htmlType="submit" block>
          {texts.signOut}
        </Button>
      </form>
      {backUrl && <Typography.Link href={backUrl}>{texts.backToApplication}</Typography.Link>}
    </IdentityFrame>
  )
}
