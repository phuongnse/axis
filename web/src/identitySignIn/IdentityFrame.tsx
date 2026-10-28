import { Button, Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { SiteMark } from '../platform/SiteMark'
import { useDesignTokens } from '../platform/themeMode'
import type { SignInLocale } from './signInContext'

/** The frame of the identity provider's pages: the Axis mark above one card, with the language links and the copyright line below. */
export function IdentityFrame({
  title,
  locales,
  children,
}: {
  title: string
  locales: SignInLocale[]
  children: ReactNode
}) {
  const { colour, spacing } = useDesignTokens()
  const [year] = useState(() => new Date().getFullYear())
  return (
    <Flex
      vertical
      align="center"
      gap={spacing.md}
      style={{ paddingBlock: spacing.lg, background: colour.canvas, minHeight: '100%' }}
    >
      <SiteMark title="Axis" size={40} />
      <Card style={{ width: '100%', maxWidth: 400 }}>
        <Flex vertical gap={spacing.md}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {children}
        </Flex>
      </Card>
      <Flex gap={spacing.sm}>
        {locales.map((locale) => (
          <Button key={locale.url} type="link" href={locale.url}>
            {locale.label}
          </Button>
        ))}
      </Flex>
      <Typography.Text type="secondary">{`© ${year} Axis`}</Typography.Text>
    </Flex>
  )
}
