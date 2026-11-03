import { Typography } from 'antd'
import { useEffect, useRef } from 'react'
import { IdentityFrame } from './IdentityFrame'
import type { FrontChannelSignOutContext } from './signInContext'

/**
 * The front-channel sign-out: a hidden frame loads each client's sign-out address, then the page goes on to the
 * redirect address, at once when there are no clients. Continue is the way out when a frame never loads.
 */
export function FrontChannelSignOutPage({ context }: { context: FrontChannelSignOutContext }) {
  const { title, texts, clients, redirectUrl } = context
  const loaded = useRef(0)
  const wentOn = useRef(false)
  const goOn = () => {
    // StrictMode runs the effect twice, and the page must go on once.
    if (redirectUrl && !wentOn.current) {
      wentOn.current = true
      window.location.replace(redirectUrl)
    }
  }
  useEffect(() => {
    if (clients.length === 0) {
      goOn()
    }
    // The context never changes while the page is open.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
  return (
    <IdentityFrame title={title} frame={context}>
      <Typography.Paragraph style={{ margin: 0 }}>{texts.message}</Typography.Paragraph>
      {clients.map(({ name, url }) => (
        <iframe
          key={`${name} ${url}`}
          hidden
          src={url}
          title={name}
          onLoad={() => {
            loaded.current += 1
            if (loaded.current >= clients.length) {
              goOn()
            }
          }}
        />
      ))}
      {redirectUrl && <Typography.Link href={redirectUrl}>{texts.continue}</Typography.Link>}
    </IdentityFrame>
  )
}
