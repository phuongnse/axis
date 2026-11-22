import { Flex, Form } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisButton } from '../platform/AxisButton'
import { useId, useRef } from 'react'
import { AcceptTermsField } from '../platform/AcceptTermsField'
import { useDesignTokens } from '../platform/themeMode'
import { IdentityFrame } from './IdentityFrame'
import type { TermsContext } from './signInContext'

/**
 * The page of Keycloak's Terms and Conditions step, which a tenant's first sign-in opens when the tenant sets terms. One
 * required checkbox, drawn by `AcceptTermsField`, names the Terms of service and the Privacy policy as links to the
 * tenant's addresses, which open in a new tab so the person keeps the step. A tenant with no privacy policy names only the Terms of service. It is a real
 * `<form method="post">` to the login address. Continue posts `accept=true` once the box is ticked, and the browser stops
 * a Continue with the box unticked. A second form below posts `cancel=true`, which Keycloak treats as Decline and answers
 * with `error=access_denied`.
 */
export function TermsPage({ context }: { context: TermsContext }) {
  const { texts, error, loginAction, termsUrl, privacyUrl } = context
  const { spacing } = useDesignTokens()
  const formId = useId()
  const submitRef = useRef<HTMLInputElement>(null)
  return (
    <IdentityFrame title={texts.title} frame={context}>
      {error && <AxisAlert kind="error" message={error} />}
      <Form
        name={formId}
        method="post"
        action={loginAction}
        layout="vertical"
        requiredMark={false}
        scrollToFirstError={{ focus: true }}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        <Flex vertical gap={spacing.lg}>
          <AcceptTermsField
            name="accept"
            text={privacyUrl ? texts.accept : texts.acceptTermsOnly}
            documents={{
              terms: { label: texts.termsOfService, href: termsUrl },
              privacy: { label: texts.privacyPolicy, href: privacyUrl },
            }}
            requiredMessage={texts.required}
          />
          <input ref={submitRef} type="hidden" name="accept" value="true" />
          <AxisButton role="finish" htmlType="submit" block>
            {texts.continue}
          </AxisButton>
        </Flex>
      </Form>
      <form method="post" action={loginAction}>
        <input type="hidden" name="cancel" value="true" />
        <AxisButton role="secondary" htmlType="submit" block>
          {texts.decline}
        </AxisButton>
      </form>
    </IdentityFrame>
  )
}
