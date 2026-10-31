import { Alert, Button, Flex, Typography } from 'antd'
import { useEffect, useRef } from 'react'
import { IdentityFrame } from './IdentityFrame'
import type { MessageAction, MessageContext } from './signInContext'

/** A post action as a real `<form method="post">` with its hidden fields. */
function PostAction({ action, formRef }: { action: MessageAction; formRef?: React.Ref<HTMLFormElement> }) {
  return (
    <form ref={formRef} method="post" action={action.href}>
      {Object.entries(action.fields ?? {}).map(([name, value]) => (
        <input key={name} type="hidden" name={name} value={value} />
      ))}
      <Button
        type={action.primary ? 'primary' : 'link'}
        htmlType="submit"
        block={action.primary}
        style={action.primary ? undefined : { paddingInline: 0 }}
      >
        {action.label}
      </Button>
    </form>
  )
}

/**
 * Proposed: the message page of the identity provider: a title, a message and its ways out, in the frame of every
 * sign-in page. It draws Keycloak's sign-out question (Sign out posts the session code), its notices, such as the one
 * after "Send link" or before an invitation's steps, its errors, as an error alert, the page that has expired, the
 * front-channel sign-out, which loads each application's sign-out address in a hidden frame and then goes on, and the
 * SAML redirect, which posts its form at once. The primary action is the one solid, full-width button; the others are
 * links below it.
 */
export function MessagePage({ context }: { context: MessageContext }) {
  const { title, message, tone, actions, frames, redirect, autoSubmit } = context
  const firstPost = useRef<HTMLFormElement>(null)
  const primary = actions.filter((action) => action.primary)
  const others = actions.filter((action) => !action.primary)

  useEffect(() => {
    if (autoSubmit) {
      firstPost.current?.submit()
    }
  }, [autoSubmit])

  useEffect(() => {
    if (!redirect) {
      return
    }
    const go = () => window.location.replace(redirect)
    if (document.readyState === 'complete') {
      go()
      return
    }
    window.addEventListener('load', go)
    return () => window.removeEventListener('load', go)
  }, [redirect])

  const firstPostAction = autoSubmit ? actions.find((action) => action.kind === 'post') : undefined
  const draw = (action: MessageAction) =>
    action.kind === 'post' ? (
      <PostAction key={action.label} action={action} formRef={action === firstPostAction ? firstPost : undefined} />
    ) : action.primary ? (
      <Button key={action.label} type="primary" block href={action.href}>
        {action.label}
      </Button>
    ) : (
      <Typography.Link key={action.label} href={action.href}>
        {action.label}
      </Typography.Link>
    )

  return (
    <IdentityFrame
      title={title}
      organization={context.realmName}
      preferences={context.preferences}
      legal={context.legal}
    >
      {message &&
        (tone === 'error' ? (
          <Alert type="error" showIcon title={message} />
        ) : (
          <Typography.Paragraph style={{ margin: 0 }}>{message}</Typography.Paragraph>
        ))}
      {frames?.map((src) => (
        <iframe key={src} src={src} title={src} style={{ display: 'none' }} />
      ))}
      {primary.map(draw)}
      {others.length > 0 && (
        <Flex gap="middle" wrap>
          {others.map(draw)}
        </Flex>
      )}
    </IdentityFrame>
  )
}
