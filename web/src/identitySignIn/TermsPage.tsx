import { Button, Checkbox, Form, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useId, useRef } from 'react'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { TermsContext } from './signInContext'

/**
 * The page of Keycloak's Terms and Conditions step, which a tenant's first sign-in opens when the tenant sets terms. One
 * required checkbox names the Terms of service and the Privacy policy as links to the tenant's addresses, which open in a
 * new tab so the person keeps the step. A tenant with no privacy policy names only the Terms of service. It is a real
 * `<form method="post">` to the login address. Continue posts `accept=true` once the box is ticked, and the browser stops
 * a Continue with the box unticked. A second form below posts `cancel=true`, which Keycloak treats as Decline and answers
 * with `error=access_denied`.
 */
export function TermsPage({ context }: { context: TermsContext }) {
  const { texts, error, loginAction, termsUrl, privacyUrl } = context
  const { colour } = useDesignTokens()
  const formId = useId()
  const submitRef = useRef<HTMLInputElement>(null)
  const link = (label: string, href: string | undefined) =>
    href ? (
      <Typography.Link href={href} target="_blank" rel="noopener noreferrer" style={{ color: colour.primaryText }}>
        {label}
      </Typography.Link>
    ) : (
      label
    )
  const label = (privacyUrl ? texts.accept : texts.acceptTermsOnly)
    .split(/(\{terms\}|\{privacy\})/)
    .map((part, index) => {
      if (part === '{terms}') {
        return <span key={index}>{link(texts.termsOfService, termsUrl)}</span>
      }
      if (part === '{privacy}') {
        return <span key={index}>{link(texts.privacyPolicy, privacyUrl)}</span>
      }
      return part
    })
  return (
    <IdentityFrame title={texts.title} frame={context}>
      {error && <AxisAlert kind="error" message={error} />}
      <Form
        name={formId}
        method="post"
        action={loginAction}
        layout="vertical"
        requiredMark={false}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        <Form.Item
          name="accept"
          valuePropName="checked"
          rules={[
            {
              validator: (_, value) => (value === true ? Promise.resolve() : Promise.reject(new Error(texts.required))),
            },
          ]}
        >
          <Checkbox>{label}</Checkbox>
        </Form.Item>
        <input ref={submitRef} type="hidden" name="accept" value="true" />
        {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
        <Button type="primary" htmlType="submit" block>
          {texts.continue}
        </Button>
      </Form>
      <form method="post" action={loginAction}>
        <input type="hidden" name="cancel" value="true" />
        {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
        <Button htmlType="submit" block>
          {texts.decline}
        </Button>
      </form>
    </IdentityFrame>
  )
}
