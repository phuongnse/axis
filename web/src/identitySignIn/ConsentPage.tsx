import { Alert, Typography } from 'antd'
import { AxisButton } from '../platform/AxisButton'
import { FilledText } from '../platform/FilledText'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { ConsentContext } from './signInContext'

/**
 * The page of Keycloak's consent step, which a client that requires consent opens before it gets a code. It names the
 * client in bold, shows the client's home page as its address so a person can tell a client with a misleading name, and
 * lists the consent text of each scope the client asks for. Allow and Deny are two real `<form method="post">` to the
 * consent address, each with the hidden `code`. Allow posts `accept=yes`. Deny posts `cancel=no`, which Keycloak treats
 * as a refusal and answers with `error=access_denied` and no code.
 */
export function ConsentPage({ context }: { context: ConsentContext }) {
  const { texts, error, oauthAction, code, clientName, homeUrl, scopes } = context
  const { colour } = useDesignTokens()
  return (
    <IdentityFrame title={texts.title} frame={context}>
      {/* oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert. */}
      {error && <Alert type="error" showIcon title={error} />}
      <Typography.Paragraph style={{ margin: 0 }}>
        <FilledText text={texts.intro} parts={{ client: <strong>{clientName}</strong> }} />
      </Typography.Paragraph>
      {homeUrl && (
        <Typography.Link href={homeUrl} target="_blank" rel="noopener noreferrer" style={{ color: colour.primaryText }}>
          {homeUrl}
        </Typography.Link>
      )}
      <ul style={{ margin: 0 }}>
        {scopes.map((scope, index) => (
          <li key={index}>{scope}</li>
        ))}
      </ul>
      <form method="post" action={oauthAction}>
        <input type="hidden" name="code" value={code} />
        <input type="hidden" name="accept" value="yes" />
        <AxisButton role="finish" htmlType="submit" block>
          {texts.allow}
        </AxisButton>
      </form>
      <form method="post" action={oauthAction}>
        <input type="hidden" name="code" value={code} />
        <input type="hidden" name="cancel" value="no" />
        <AxisButton role="secondary" htmlType="submit" block>
          {texts.deny}
        </AxisButton>
      </form>
    </IdentityFrame>
  )
}
