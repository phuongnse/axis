import { Card, ConfigProvider, Flex, Typography } from 'antd'
import type { ReactNode, RefObject } from 'react'
import { antLocaleFor } from '../src/platform/antLocale'
import { OrganizationHeading } from '../src/platform/OrganizationHeading'
import { PreferencesMenu } from '../src/platform/PreferencesMenu'
import { useDesignTokens } from '../src/platform/themeMode'
import { signUpLocales, type SignUpLocale } from './texts'

/**
 * The card that every page of the sign-up bundle draws: the preferences menu, the organization heading and a titled card
 * that holds the page's content. The title takes the focus when its `titleRef` is focused, as a page moves to its next step.
 */
export function SignUpFrame({
  locale,
  onLocaleChange,
  title,
  titleRef,
  children,
}: {
  locale: SignUpLocale
  onLocaleChange: (locale: SignUpLocale) => void
  title: string
  titleRef: RefObject<HTMLElement | null>
  children: ReactNode
}) {
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <ConfigProvider locale={antLocaleFor(locale)}>
      <Flex
        vertical
        align="center"
        gap={spacing.md}
        style={{
          position: 'relative',
          paddingBlock: spacing.lg,
          background: colour.canvas,
          minHeight: '100vh',
          boxSizing: 'border-box',
        }}
      >
        <div style={{ position: 'absolute', top: spacing.lg, right: spacing.lg }}>
          <PreferencesMenu
            locales={signUpLocales}
            locale={locale}
            onLocaleChange={(next) => onLocaleChange(next as SignUpLocale)}
          />
        </div>
        <OrganizationHeading />
        <Card
          style={{
            width: '100%',
            maxWidth: 480,
            background: colour.surface,
            borderColor: colour.borderSubtle,
            borderRadius: radius.base,
          }}
          styles={{ body: { padding: spacing.xl } }}
        >
          <Flex vertical gap={spacing.md}>
            <Typography.Title ref={titleRef} level={4} tabIndex={-1} style={{ margin: 0 }}>
              {title}
            </Typography.Title>
            {children}
          </Flex>
        </Card>
      </Flex>
    </ConfigProvider>
  )
}
