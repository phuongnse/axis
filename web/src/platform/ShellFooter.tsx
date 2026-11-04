import { Layout } from 'antd'
import { useEffect, useState } from 'react'
import { fetchAboutVersion } from './about'
import { LegalLine } from './LegalLine'
import type { SiteLegal } from './site'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * The slim footer across the bottom of every site. At the start it holds the copyright, with the server's version
 * for a signed-in person only. At the end it holds the legal line: Privacy and Terms when `legal` gives their
 * addresses, and Licences always.
 */
export function ShellFooter({ legal }: { legal?: SiteLegal | null }) {
  const { colour, spacing } = useDesignTokens()
  const t = useText()
  const [year] = useState(() => new Date().getFullYear())
  // `null` when nobody is signed in, `undefined` while it loads or when loading failed.
  const [version, setVersion] = useState<string | null | undefined>(undefined)

  useEffect(() => {
    const controller = new AbortController()
    fetchAboutVersion(controller.signal).then(setVersion, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the server version failed', error)
      }
    })
    return () => controller.abort()
  }, [])

  const copyright = `© ${year} Axis${version != null ? ` · v${version}` : ''}`

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
