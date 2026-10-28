import { Alert, Avatar, Badge, Empty, Flex, Spin, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { startSignIn } from '../platform/api'
import { AxisButton } from '../platform/AxisButton'
import { PageLayout } from '../platform/PageLayout'
import { useServerStatus, type ServerStatus } from '../platform/serverStatus'
import { fetchSites, type SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { usePlatform } from './context'

const statusBadge: Record<ServerStatus, 'processing' | 'success' | 'error'> = {
  checking: 'processing',
  ready: 'success',
  unavailable: 'error',
}

/** The site title in the current locale, or in the site's first locale when it lacks the current one. */
function siteTitle(site: SiteListItem, locale: string): string {
  return site.titles[locale] ?? Object.values(site.titles)[0] ?? site.path
}

/** Proposed: a site is a tile with its mark, its title and its path, and the whole tile opens the site. */
function SiteTile({ site, locale }: { site: SiteListItem; locale: string }) {
  const { colour, spacing, radius } = useDesignTokens()
  const title = siteTitle(site, locale)
  return (
    <Link
      to={`/${site.path}`}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: spacing.sm,
        padding: spacing.md,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        color: colour.text,
      }}
    >
      <Avatar
        aria-hidden
        shape="square"
        size={40}
        style={{ flex: 'none', background: colour.primary, fontWeight: 600 }}
      >
        {title.trim().charAt(0).toUpperCase()}
      </Avatar>
      <Flex vertical style={{ minWidth: 0 }}>
        <Typography.Text strong ellipsis>
          {title}
        </Typography.Text>
        <Typography.Text type="secondary" ellipsis>
          /{site.path}
        </Typography.Text>
      </Flex>
    </Link>
  )
}

/** Proposed: with nobody signed in, the sites give way to one panel that asks the person to sign in. */
function SignInPanel() {
  const t = useText()
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <Flex
      vertical
      align="center"
      gap={spacing.md}
      style={{
        padding: spacing.xl,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
      }}
    >
      <Typography.Text data-testid="sites-sign-in">{t('shell.home.sites.signIn')}</Typography.Text>
      <AxisButton role="finish" onClick={startSignIn}>
        {t('shell.account.signIn')}
      </AxisButton>
    </Flex>
  )
}

function SiteList() {
  const t = useText()
  const { spacing } = useDesignTokens()
  const { locale } = usePlatform().current
  // `null` when nobody is signed in.
  const [sites, setSites] = useState<SiteListItem[] | null>()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchSites(controller.signal)
      .then(setSites)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the sites failed', error)
          setFailed(true)
        }
      })
    return () => controller.abort()
  }, [])

  if (sites === null) {
    return <SignInPanel />
  }

  return (
    <section aria-labelledby="home-sites">
      <Typography.Title id="home-sites" level={5} style={{ marginTop: 0 }}>
        {t('shell.home.sites.title')}
      </Typography.Title>
      {failed ? (
        <Alert data-testid="sites-error" type="error" title={t('shell.home.sites.loadFailed')} />
      ) : sites === undefined ? (
        <Spin />
      ) : sites.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.home.sites.empty')} />
      ) : (
        // Proposed: the tiles fill a grid of columns at least 240 px wide.
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(240px, 1fr))', gap: spacing.md }}>
          {sites.map((site) => (
            <SiteTile key={site.path} site={site} locale={locale} />
          ))}
        </div>
      )}
    </section>
  )
}

/** Proposed: the server's readiness is a quiet line at the foot of the home page, not a card of its own. */
function StatusFooter() {
  const t = useText()
  const serverStatus = useServerStatus()
  const { colour, spacing } = useDesignTokens()
  return (
    <footer style={{ marginTop: spacing.lg, paddingTop: spacing.md, borderTop: `1px solid ${colour.divider}` }}>
      <Typography.Text type="secondary">
        {t('shell.serverStatus.title')}:{' '}
        <Badge
          data-testid="server-status"
          status={statusBadge[serverStatus]}
          text={<Typography.Text type="secondary">{t(`shell.serverStatus.${serverStatus}`)}</Typography.Text>}
        />
      </Typography.Text>
    </footer>
  )
}

export function HomePage() {
  const t = useText()
  const [searchParams] = useSearchParams()
  const signInFailed = searchParams.get('signIn') === 'failed'

  return (
    <PageLayout title={t('shell.home.title')}>
      <Typography.Text type="secondary">{t('shell.home.subtitle')}</Typography.Text>
      {signInFailed && (
        <Alert data-testid="sign-in-failed" role="alert" type="warning" showIcon title={t('shell.home.signInFailed')} />
      )}
      <SiteList />
      <StatusFooter />
    </PageLayout>
  )
}
