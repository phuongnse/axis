import { DownOutlined } from '@ant-design/icons'
import { Button, Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useState } from 'react'
import { OrganizationHeading } from '../platform/OrganizationHeading'
import { nativeLanguageName } from '../platform/languageName'
import { PreferencesDropdown } from '../platform/PreferencesMenu'
import { useDesignTokens } from '../platform/themeMode'
import type { SignInPreferences } from './signInContext'

/**
 * The frame of the identity provider's pages: the organization's mark and name above one card, with the language and
 * theme button and the copyright line below.
 */
export function IdentityFrame({
  title,
  organization,
  preferences,
  children,
}: {
  title: string
  /** The organization's name for the heading above the card. Absent means Axis. */
  organization?: string
  preferences: SignInPreferences
  children: ReactNode
}) {
  const { colour, spacing, radius } = useDesignTokens()
  const [year] = useState(() => new Date().getFullYear())
  const language = nativeLanguageName(preferences.locale)
  // A design-system frame sets --shell-min-height to its own height, so the page fills it.
  return (
    <Flex
      vertical
      align="center"
      gap={spacing.md}
      style={{
        paddingBlock: spacing.lg,
        background: colour.canvas,
        minHeight: 'var(--shell-min-height, 100%)',
        boxSizing: 'border-box',
      }}
    >
      <OrganizationHeading name={organization} />
      <Card
        style={{
          width: '100%',
          maxWidth: 400,
          background: colour.surface,
          borderColor: colour.borderSubtle,
          borderRadius: radius.base,
        }}
        styles={{ body: { padding: spacing.xl } }}
      >
        <Flex vertical gap={spacing.md}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {children}
        </Flex>
      </Card>
      {/* Proposed: one text button that names the page's language in that language, with a caret, opens the shell's
          language and theme menu, so every language is one row in it and the theme can change before sign-in. */}
      <PreferencesDropdown
        languages={preferences.locales.map((entry) => ({ tag: entry.tag, label: nativeLanguageName(entry.tag) }))}
        language={preferences.locale}
        texts={preferences.texts}
        onLanguageChange={(tag) => {
          const target = preferences.locales.find((entry) => entry.tag === tag)
          if (target) {
            window.location.assign(target.url)
          }
        }}
      >
        <Button
          type="text"
          aria-label={`${language}, ${preferences.texts.label}`}
          style={{ paddingInline: spacing.xs }}
        >
          <span style={{ display: 'flex', alignItems: 'center', gap: spacing.xs }}>
            {language}
            <DownOutlined aria-hidden style={{ color: colour.textMuted, fontSize: 12 }} />
          </span>
        </Button>
      </PreferencesDropdown>
      <Typography.Text type="secondary">{`© ${year} Axis`}</Typography.Text>
    </Flex>
  )
}
