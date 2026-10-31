import { Alert, Button, Checkbox, Form, Typography } from 'antd'
import { Fragment, useId, useRef, type ReactNode } from 'react'
import { IdentityFrame } from './IdentityFrame'
import type { TermsContext } from './signInContext'

/** The sentence with `{terms}` and `{privacy}` replaced by links that open in a new tab, so the form keeps its state. */
function withLinks(sentence: string, links: Record<string, ReactNode>): ReactNode[] {
  return sentence.split(/(\{terms\}|\{privacy\})/).map((part, index) => {
    const link = links[part]
    return <Fragment key={index}>{link ?? part}</Fragment>
  })
}

/**
 * Proposed: the terms page, which Keycloak's "Terms and Conditions" required action shows after a person's first
 * sign-in and again when the tenant's terms change version. It names the tenant's terms of service and privacy policy
 * as links in one required checkbox; a site's own links change only the legal line under the card. Continue is a real
 * `<form method="post">` to the login address with `accept`, and Keycloak records when the person accepted. "Decline and sign out" posts `cancel`, which ends the sign-in. The legal
 * links stay under the card, as on every page.
 */
export function TermsPage({ context }: { context: TermsContext }) {
  const { texts, error, loginAction, legal, accept } = context
  const formId = useId()
  const [form] = Form.useForm<{ agree?: boolean }>()
  const submitRef = useRef<HTMLInputElement>(null)
  const link = (href: string, label: string) => (
    <Typography.Link href={href} target="_blank" rel="noreferrer">
      {label}
    </Typography.Link>
  )
  const { privacy, terms } = accept
  const sentence = terms && privacy ? texts.agreeBoth : terms ? texts.agreeTerms : texts.agreePrivacy
  const label = withLinks(sentence, {
    '{terms}': terms ? link(terms, texts.termsLink) : null,
    '{privacy}': privacy ? link(privacy, texts.privacyLink) : null,
  })

  return (
    <IdentityFrame title={texts.title} organization={context.realmName} preferences={context.preferences} legal={legal}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {error && <Alert type="error" showIcon title={error} />}
      <Form
        form={form}
        name={formId}
        method="post"
        action={loginAction}
        layout="vertical"
        requiredMark={false}
        // Ant Design validates and stops the submit event, so the native post happens here.
        onFinish={() => submitRef.current?.form?.submit()}
      >
        <Form.Item
          name="agree"
          valuePropName="checked"
          rules={[
            {
              validator: (_, value) => (value ? Promise.resolve() : Promise.reject(new Error(texts.agreeRequired))),
            },
          ]}
        >
          <Checkbox aria-required>{label}</Checkbox>
        </Form.Item>
        <input ref={submitRef} type="hidden" name="accept" value="accept" />
        <Button type="primary" htmlType="submit" block>
          {texts.accept}
        </Button>
      </Form>
      <form method="post" action={loginAction}>
        <input type="hidden" name="cancel" value="cancel" />
        <Button type="link" htmlType="submit" style={{ paddingInline: 0 }}>
          {texts.decline}
        </Button>
      </form>
    </IdentityFrame>
  )
}
