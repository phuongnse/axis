import { Button, Typography } from 'antd'
import { useEffect, useRef } from 'react'
import { IdentityFrame } from './IdentityFrame'
import type { SamlPostContext } from './signInContext'

/**
 * The SAML redirect: a short card and a real `<form method="post">` to `action` with the hidden `SAMLRequest`,
 * `SAMLResponse` and `RelayState` that Keycloak gave. The form posts at once, and Continue posts it by hand.
 */
export function SamlPostPage({ context }: { context: SamlPostContext }) {
  const { title, texts, action, samlRequest, samlResponse, relayState } = context
  const formRef = useRef<HTMLFormElement>(null)
  const posted = useRef(false)
  useEffect(() => {
    // StrictMode runs the effect twice, and the form must post once.
    if (!posted.current) {
      posted.current = true
      formRef.current?.submit()
    }
  }, [])
  return (
    <IdentityFrame title={title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      <form ref={formRef} method="post" action={action}>
        {samlRequest !== undefined && <input type="hidden" name="SAMLRequest" value={samlRequest} />}
        {samlResponse !== undefined && <input type="hidden" name="SAMLResponse" value={samlResponse} />}
        {relayState !== undefined && <input type="hidden" name="RelayState" value={relayState} />}
        {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
        <Button type="primary" htmlType="submit" block>
          {texts.continue}
        </Button>
      </form>
    </IdentityFrame>
  )
}
