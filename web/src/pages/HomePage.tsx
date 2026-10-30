import { Alert, Button, Card, Empty, Flex, Spin, Typography } from 'antd'
import { createElement, useEffect, useId, useMemo, useState, type CSSProperties, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { startSignIn } from '../platform/api'
import { fetchCurrentUser, type CurrentUser } from '../platform/currentUser'
import { colourTokens } from '../platform/designTokens'
import { navigationIcon } from '../platform/navigationIcons'
import { OrganizationHeading } from '../platform/OrganizationHeading'
import { SiteMark } from '../platform/SiteMark'
import { applicationTitle, fetchSites, siteTitle, type SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useNarrowWidth } from '../platform/useNarrowWidth'
import { usePlatform } from './context'
import { siteAddress } from '../platform/siteLinks'

/** What the page loaded. `null` means that the call answered 401, and `'failed'` that it failed otherwise. */
interface Loaded {
  user: CurrentUser | null | 'failed'
  sites: SiteListItem[] | null | 'failed'
}

function useHomeGrid(): CSSProperties {
  const narrow = useNarrowWidth()
  const { spacing } = useDesignTokens()
  return {
    display: 'grid',
    gridTemplateColumns: narrow ? '1fr' : 'repeat(3, minmax(0, 1fr))',
    gap: spacing.md,
  }
}

export function SignInCard() {
  const t = useText()
  const [searchParams] = useSearchParams()
  const { colour, spacing } = useDesignTokens()
  const signInFailed = searchParams.get('signIn') === 'failed'

  return (
    <Flex vertical align="center" gap={spacing.md} style={{ paddingBlock: spacing.xl }}>
      <OrganizationHeading />
      <Card
        data-testid="sign-in-card"
        style={{ width: '100%', maxWidth: 420, border: `1px solid ${colour.borderSubtle}` }}
        styles={{ body: { padding: spacing.xl } }}
      >
        <Flex vertical gap={spacing.md}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {t('shell.home.signIn.title')}
          </Typography.Title>
          <Typography.Paragraph style={{ margin: 0, color: colour.textMuted }}>
            {t('shell.home.signIn.message')}
          </Typography.Paragraph>
          {signInFailed && (
            <Alert
              data-testid="sign-in-failed"
              role="alert"
              type="warning"
              showIcon
              title={t('shell.home.signInFailed')}
            />
          )}
          <Button type="primary" block onClick={startSignIn}>
            {t('shell.account.signIn')}
          </Button>
        </Flex>
      </Card>
    </Flex>
  )
}

function ToolTile({ to, icon, title, description }: { to: string; icon: string; title: string; description: string }) {
  const { colour, spacing, radius } = useDesignTokens()
  return (
    <Link
      to={to}
      style={{
        display: 'flex',
        alignItems: 'flex-start',
        gap: spacing.sm,
        padding: spacing.md,
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
      }}
    >
      <span
        data-testid="tool-icon"
        style={{
          display: 'grid',
          placeItems: 'center',
          flex: 'none',
          width: 32,
          height: 32,
          background: colour.primary,
          color: colourTokens.surface.light,
          borderRadius: radius.base,
        }}
      >
        {createElement(navigationIcon(icon), { 'aria-hidden': true, style: { fontSize: 20 } })}
      </span>
      <Flex vertical>
        <Typography.Text strong>{title}</Typography.Text>
        <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>
      </Flex>
    </Link>
  )
}

function Tools({ user }: { user: CurrentUser }) {
  const t = useText()
  const grid = useHomeGrid()
  if (!user.studio && !user.console && !user.users) {
    return null
  }
  return (
    <div data-testid="home-grid" style={grid}>
      {user.studio && (
        <ToolTile
          to="/studio"
          icon="tool"
          title={t('shell.appSwitcher.studio')}
          description={t('shell.home.studio.description')}
        />
      )}
      {user.console && (
        <ToolTile
          to="/console"
          icon="dashboard"
          title={t('shell.appSwitcher.console')}
          description={t('shell.home.console.description')}
        />
      )}
      {user.users && (
        <ToolTile
          to="/users"
          icon="people"
          title={t('shell.appSwitcher.users')}
          description={t('shell.home.users.description')}
        />
      )}
    </div>
  )
}

function WelcomeBand({ user }: { user: CurrentUser | 'failed' }) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  return (
    <div
      style={{
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        padding: spacing.lg,
      }}
    >
      <Flex vertical gap={spacing.md}>
        <Flex vertical gap={spacing.xs}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {t('shell.home.title')}
          </Typography.Title>
          <Typography.Text style={{ color: colour.textMuted }}>{t('shell.home.subtitle')}</Typography.Text>
        </Flex>
        {user !== 'failed' && <Tools user={user} />}
      </Flex>
    </div>
  )
}

function ApplicationCard({ title, sites, locale }: { title: string; sites: SiteListItem[]; locale: string }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const grid = useHomeGrid()
  const headingId = useId()
  const count = sites.length
  const sorted = [...sites].sort((a, b) => siteTitle(a, locale).localeCompare(siteTitle(b, locale), locale))

  return (
    <Card styles={{ body: { padding: spacing.md } }}>
      <section aria-labelledby={headingId}>
        <Flex vertical gap={spacing.md}>
          <Flex
            data-testid="application-card-header"
            align="center"
            gap={spacing.sm}
            style={{ paddingBottom: spacing.sm, borderBottom: `1px solid ${colour.divider}` }}
          >
            <SiteMark title={title} size={32} />
            <Typography.Title level={5} id={headingId} style={{ margin: 0 }}>
              {title}
            </Typography.Title>
            <Typography.Text style={{ color: colour.textMuted, marginInlineStart: 'auto' }}>
              {`${count} ${t(count === 1 ? 'shell.home.applications.site' : 'shell.home.applications.sites')}`}
            </Typography.Text>
          </Flex>
          <div data-testid="home-grid" style={grid}>
            {sorted.map((site) => (
              <Link
                key={site.path}
                to={siteAddress(site.path)}
                style={{ display: 'flex', alignItems: 'center', gap: spacing.sm, minWidth: 0, padding: spacing.xs }}
              >
                <SiteMark title={siteTitle(site, locale)} size={24} />
                <Typography.Text ellipsis style={{ color: colour.text }}>
                  {siteTitle(site, locale)}
                </Typography.Text>
              </Link>
            ))}
          </div>
        </Flex>
      </section>
    </Card>
  )
}

function Applications({ sites, locale }: { sites: SiteListItem[] | 'failed'; locale: string }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const groups = useMemo(() => {
    if (sites === 'failed') {
      return []
    }
    const byName = new Map<string, { title: string; sites: SiteListItem[] }>()
    for (const site of sites) {
      const group = byName.get(site.application.name)
      if (group) {
        group.sites.push(site)
      } else {
        byName.set(site.application.name, { title: applicationTitle(site.application, locale), sites: [site] })
      }
    }
    return [...byName.entries()]
      .map(([name, group]) => ({ name, ...group }))
      .sort((a, b) => a.title.localeCompare(b.title, locale))
  }, [sites, locale])

  let body: ReactNode
  if (sites === 'failed') {
    body = <Alert data-testid="applications-error" type="error" title={t('shell.home.applications.loadFailed')} />
  } else if (groups.length === 0) {
    body = <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.home.applications.empty')} />
  } else {
    body = groups.map((group) => (
      <ApplicationCard key={group.name} title={group.title} sites={group.sites} locale={locale} />
    ))
  }

  return (
    <Flex vertical gap={spacing.md}>
      <Flex align="center" gap={spacing.xs}>
        <Typography.Title level={5} style={{ margin: 0 }}>
          {t('shell.home.applications.title')}
        </Typography.Title>
        {sites !== 'failed' && <Typography.Text style={{ color: colour.textMuted }}>{groups.length}</Typography.Text>}
      </Flex>
      {body}
    </Flex>
  )
}

export function HomePage() {
  const { locale } = usePlatform().current
  const { spacing } = useDesignTokens()
  const [loaded, setLoaded] = useState<Loaded>()

  useEffect(() => {
    const controller = new AbortController()
    Promise.allSettled([fetchCurrentUser(controller.signal), fetchSites(controller.signal)]).then(([user, sites]) => {
      if (controller.signal.aborted) {
        return
      }
      if (user.status === 'rejected') {
        console.warn('Loading the current user failed', user.reason)
      }
      if (sites.status === 'rejected') {
        console.warn('Loading the sites failed', sites.reason)
      }
      setLoaded({
        user: user.status === 'fulfilled' ? user.value : 'failed',
        sites: sites.status === 'fulfilled' ? sites.value : 'failed',
      })
    })
    return () => controller.abort()
  }, [])

  if (loaded === undefined) {
    return <Spin />
  }
  // Either call answering 401 means the session is gone, even if the other call went through before it ended.
  if (loaded.user === null || loaded.sites === null) {
    return <SignInCard />
  }

  return (
    <Flex vertical gap={spacing.lg}>
      <WelcomeBand user={loaded.user} />
      <Applications sites={loaded.sites} locale={locale} />
    </Flex>
  )
}
