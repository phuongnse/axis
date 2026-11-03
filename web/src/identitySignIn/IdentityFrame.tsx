import { Card, Flex, Typography } from 'antd'
import type { ReactNode } from 'react'
import { useMemo, useState } from 'react'
import { LegalLine } from '../platform/LegalLine'
import { OrganizationHeading } from '../platform/OrganizationHeading'
import { PreferencesMenu } from '../platform/PreferencesMenu'
import { TextContext } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import type { IdentityFrameContext } from './signInContext'

/**
 * The frame of the identity provider's pages: the organization's mark and name above one card, with the shell's
 * "Language and theme" gear at the top right and the legal line below.
 */
export function IdentityFrame({
  title,
  frame,
  children,
}: {
  title: string
  frame: IdentityFrameContext
  children: ReactNode
}) {
  const { colour, spacing, radius } = useDesignTokens()
  const [year] = useState(() => new Date().getFullYear())
  // Keycloak pages have no text catalogue, so the gear's keys resolve from the page's context.
  const frameText = useMemo(() => {
    const { frameTexts, locales } = frame
    const texts = new Map<string, string>([
      ['shell.preferences.label', frameTexts.languageAndTheme],
      ['shell.locale.label', frameTexts.language],
      ['shell.theme.label', frameTexts.theme],
      ['shell.theme.system', frameTexts.system],
      ['shell.theme.light', frameTexts.light],
      ['shell.theme.dark', frameTexts.dark],
      ...locales.map(({ tag, label }): [string, string] => [`shell.locale.${tag}`, label]),
    ])
    return (key: string) => texts.get(key) ?? key
  }, [frame])
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
      <div style={{ position: 'absolute', top: spacing.lg, right: spacing.lg }}>
        <TextContext.Provider value={frameText}>
          <PreferencesMenu
            locales={frame.locales.map((locale) => locale.tag)}
            locale={frame.locale}
            onLocaleChange={(tag) => {
              const target = frame.locales.find((locale) => locale.tag === tag)
              if (target) {
                window.location.assign(target.url)
              }
            }}
          />
        </TextContext.Provider>
      </div>
      <OrganizationHeading name={frame.realmName} />
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
      <div style={{ width: '100%', maxWidth: 400 }}>
        <LegalLine copyright={`© ${year} Axis`} links={frame.legal} />
      </div>
    </Flex>
  )
}
