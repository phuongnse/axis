import { Layout, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { fetchAboutVersion } from './about'
import { useDesignTokens } from './themeMode'

/**
 * The slim footer across the bottom of every site: the copyright, with the server's version for a
 * signed-in person only.
 */
export function ShellFooter() {
  const { colour, spacing } = useDesignTokens()
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
      <Typography.Text style={{ color: colour.textMuted }}>{copyright}</Typography.Text>
    </Layout.Footer>
  )
}
