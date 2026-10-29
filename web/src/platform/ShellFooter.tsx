import { Badge, Flex, Layout, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { fetchAboutVersion } from './about'
import { statusBadge, useServerStatus } from './serverStatus'
import { useText } from './texts'
import { useDesignTokens } from './themeMode'

/**
 * The slim footer across the bottom of every site: the copyright, with the server's version for a
 * signed-in person only, and the server's readiness.
 */
export function ShellFooter() {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const status = useServerStatus()
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
      <Flex justify="space-between" align="center" gap={spacing.md} wrap>
        <Typography.Text style={{ color: colour.textMuted }}>{copyright}</Typography.Text>
        <Flex align="center" gap={spacing.sm}>
          <Typography.Text style={{ color: colour.textMuted }}>{t('shell.serverStatus.title')}</Typography.Text>
          <Badge
            data-testid="footer-server-status"
            status={statusBadge[status]}
            text={<span style={{ color: colour.textMuted }}>{t(`shell.serverStatus.${status}`)}</span>}
          />
        </Flex>
      </Flex>
    </Layout.Footer>
  )
}
