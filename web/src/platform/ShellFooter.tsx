import { Layout } from 'antd'
import { useState } from 'react'
import { useAboutVersion } from './about'
import { LegalLine } from './LegalLine'
import type { SiteLegal } from './site'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * The slim footer across the bottom of every site. At the start it holds the copyright and, for a signed-in person
 * only, the server's version beside it. At the end it holds the legal line: Privacy and Terms when `legal` gives their
 * addresses, and Licences always.
 */
export function ShellFooter({ legal }: { legal?: SiteLegal | null }) {
  const { colour, spacing } = useDesignTokens()
  const t = useText()
  const [year] = useState(() => new Date().getFullYear())
  const version = useAboutVersion()

  const copyright = `© ${year} Axis`

  return (
    <Layout.Footer
      style={{
        paddingBlock: spacing.xs,
        paddingInline: spacing.lg,
        background: colour.surface,
        borderTop: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <LegalLine
        copyright={copyright}
        version={version != null ? `v${version}` : undefined}
        links={{
          privacyUrl: legal?.privacyUrl ?? undefined,
          termsUrl: legal?.termsUrl ?? undefined,
          licencesUrl: '/licences',
          texts: {
            legal: t('shell.legal.label'),
            privacy: t('shell.legal.privacy'),
            terms: t('shell.legal.terms'),
            licences: t('shell.legal.licences'),
          },
        }}
      />
    </Layout.Footer>
  )
}
