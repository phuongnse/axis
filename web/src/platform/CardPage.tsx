import { Card, Flex, Typography } from 'antd'
import type { ReactNode, RefObject } from 'react'
import { useState } from 'react'
import { LegalLine, type LegalLinks } from './LegalLine'
import { OrganizationHeading } from './OrganizationHeading'
import { PreferencesMenu } from './PreferencesMenu'
import { useDesignTokens } from './themeMode'

/**
 * A page that is one card: the organization's mark and name above the card, the "Language and theme" gear at the top
 * right and the legal line below, each drawn only when given. The card and the legal line are as wide as the `small`
 * dialog size token. No name means Axis. A frame that has its own gear and legal line leaves both out. See [which
 * widget](docs/reference/shared-widgets.md#which-widget).
 */
export function CardPage({
  organization,
  title,
  titleRef,
  preferences,
  legal,
  children,
}: {
  organization?: string
  title: string
  /** Lets the page move the focus to the title, as a page does when its next step opens. */
  titleRef?: RefObject<HTMLElement | null>
  preferences?: { locales: readonly string[]; locale: string; onLocaleChange: (locale: string) => void }
  legal?: LegalLinks
  children: ReactNode
}) {
  const { colour, spacing, radius, size } = useDesignTokens()
  const [year] = useState(() => new Date().getFullYear())
  // A design-system frame sets --shell-min-height to its own height, so the page fills it.
  return (
    <Flex
      vertical
      align="center"
      gap={spacing.md}
      style={{
        position: 'relative',
        paddingBlock: spacing.lg,
        background: colour.canvas,
        minHeight: 'var(--shell-min-height, 100%)',
        boxSizing: 'border-box',
      }}
    >
      {preferences && (
        <div style={{ position: 'absolute', top: spacing.lg, right: spacing.lg }}>
          <PreferencesMenu
            locales={preferences.locales}
            locale={preferences.locale}
            onLocaleChange={preferences.onLocaleChange}
          />
        </div>
      )}
      <OrganizationHeading name={organization} />
      <Card
        data-testid="card-page-card"
        style={{
          width: '100%',
          maxWidth: size.dialog.small,
          background: colour.surface,
          borderColor: colour.borderSubtle,
          borderRadius: radius.base,
        }}
        styles={{ body: { padding: spacing.xl } }}
      >
        <Flex vertical gap={spacing.md}>
          <Typography.Title ref={titleRef} level={4} tabIndex={titleRef ? -1 : undefined} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {children}
        </Flex>
      </Card>
      {legal && (
        <div style={{ width: '100%', maxWidth: size.dialog.small }} data-testid="card-page-legal">
          <LegalLine copyright={`© ${year} Axis`} links={legal} />
        </div>
      )}
    </Flex>
  )
}
