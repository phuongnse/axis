import { Alert, Button, Card, Empty, Flex, Spin, Tag, Typography } from 'antd'
import { createElement, useEffect, useId, useMemo, useState, type CSSProperties, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { startSignIn } from '../platform/api'
import { fetchCurrentUser, type CurrentUser } from '../platform/currentUser'
import { navigationIcon } from '../platform/navigationIcons'
import { SiteMark } from '../platform/SiteMark'
import { applicationTitle, fetchSites, siteTitle, type SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useNarrowWidth } from '../platform/useNarrowWidth'
import { usePlatform } from './context'

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

/** The padding of the welcome band that puts its content in line with the content of the cards below it. */
function useBandInset() {
  const { spacing } = useDesignTokens()
  // The content area's padding, the card's border and the card's padding.
  return `calc(${spacing.lg}px + 1px + ${spacing.lg}px)`
}

function SignInCard() {
  const t = useText()
  const [searchParams] = useSearchParams()
  const { spacing } = useDesignTokens()
  const signInFailed = searchParams.get('signIn') === 'failed'

  return (
    <Flex justify="center" align="center" style={{ paddingBlock: spacing.xl }}>
      <Card style={{ width: '100%', maxWidth: 400 }}>
        <Flex vertical gap={spacing.md}>
          <SiteMark title="Axis" size={40} />
          <Typography.Title level={4} style={{ margin: 0 }}>
            {t('shell.home.signIn.title')}
          </Typography.Title>
          <Typography.Paragraph style={{ margin: 0 }}>{t('shell.home.signIn.message')}</Typography.Paragraph>
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
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
      }}
    >
      {createElement(navigationIcon(icon), { 'aria-hidden': true, style: { fontSize: 20, marginBlockStart: 2 } })}
      <Flex vertical>
        <Typography.Text strong>{title}</Typography.Text>
        <Typography.Text type="secondary">{description}</Typography.Text>
      </Flex>
    </Link>
  )
}

function Tools({ user }: { user: CurrentUser }) {
  const t = useText()
  const grid = useHomeGrid()
  if (!user.studio && !user.console) {
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
    </div>
  )
}

function WelcomeBand({ user }: { user: CurrentUser | 'failed' }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const inset = useBandInset()
  return (
    <div
      style={{
        // Negative margins reach the edges of the content area, whose padding is spacing.lg.
        margin: `-${spacing.lg}px -${spacing.lg}px 0`,
        padding: `${spacing.md}px ${inset}`,
        background: colour.surface,
        borderBottom: `1px solid ${colour.borderSubtle}`,
      }}
    >
      <Flex vertical gap={spacing.md}>
        <Flex vertical gap={spacing.xs}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {t('shell.home.title')}
          </Typography.Title>
          <Typography.Text type="secondary">{t('shell.home.subtitle')}</Typography.Text>
        </Flex>
        {user !== 'failed' && <Tools user={user} />}
      </Flex>
    </div>
  )
}

function ApplicationCard({ title, sites, locale }: { title: string; sites: SiteListItem[]; locale: string }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const grid = useHomeGrid()
  const headingId = useId()
  const count = sites.length
  const sorted = [...sites].sort((a, b) => siteTitle(a, locale).localeCompare(siteTitle(b, locale), locale))

  return (
    <Card styles={{ body: { padding: spacing.lg } }}>
      <section aria-labelledby={headingId}>
        <Flex vertical gap={spacing.md}>
          <Flex align="center" gap={spacing.sm}>
            <SiteMark title={title} />
            <Flex vertical>
              <Typography.Text strong id={headingId}>
                {title}
              </Typography.Text>
              <Typography.Text type="secondary">
                {`${count} ${t(count === 1 ? 'shell.home.applications.site' : 'shell.home.applications.sites')}`}
              </Typography.Text>
            </Flex>
          </Flex>
          <div data-testid="home-grid" style={grid}>
            {sorted.map((site) => (
              <Link
                key={site.path}
                to={`/${site.path}`}
                style={{ display: 'flex', alignItems: 'center', gap: spacing.sm, minWidth: 0 }}
              >
                <SiteMark title={siteTitle(site, locale)} size={24} />
                <Typography.Text ellipsis style={{ color: 'inherit' }}>
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
  const { spacing } = useDesignTokens()
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
        {sites !== 'failed' && <Tag>{groups.length}</Tag>}
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
