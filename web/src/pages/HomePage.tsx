import { Alert, Button, Card, Empty, Flex, Spin, Typography } from 'antd'
import { createElement, useEffect, useId, useMemo, useState, type CSSProperties, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { isSignInCardAddress, startSignIn } from '../platform/api'
import { fetchCurrentUser, type CurrentUser } from '../platform/currentUser'
import { colourTokens } from '../platform/designTokens'
import { navigationIcon } from '../platform/navigationIcons'
import { OrganizationHeading } from '../platform/OrganizationHeading'
import { SiteMark } from '../platform/SiteMark'
import { applicationTitle, fetchSites, localized, siteTitle, type SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { useNarrowWidth } from '../platform/useNarrowWidth'
import { usePlatform } from './context'
import { homeGridColumns } from './homeGrid'
import { siteAddress } from '../platform/siteLinks'

/** What the page loaded. `null` means that the call answered 401, and `'failed'` that it failed otherwise. */
interface Loaded {
  user: CurrentUser | null | 'failed'
  sites: SiteListItem[] | null | 'failed'
  /** Which card replaces the page when a call answered 401. */
  card: SignInCardState | null
}

type SignInCardState = 'refused' | 'signedOut'

/** The card the address asks for. A person who has no session and no card address goes to sign-in instead. */
function cardOfAddress(pathname: string, search: string): SignInCardState | null {
  if (!isSignInCardAddress(pathname, search)) {
    return null
  }
  return new URLSearchParams(search).get('signIn') === 'failed' ? 'refused' : 'signedOut'
}

function useHomeGrid(): CSSProperties {
  const narrow = useNarrowWidth()
  const { spacing } = useDesignTokens()
  return {
    display: 'grid',
    gridTemplateColumns: narrow ? '1fr' : homeGridColumns(spacing.md),
    gap: spacing.md,
  }
}

/** The keyboard focus ring of a link tile. Inline styles cannot target focus. */
function HomeTileStyle() {
  const { colour } = useDesignTokens()
  return (
    <style>{`[data-home-tile]:focus-visible { outline: 2px solid ${colour.focusRing}; outline-offset: 2px; }`}</style>
  )
}

/** The card on `/` that replaces the page when nobody is signed in: after a refused sign-in or after sign-out. */
export function SignInCard({ state }: { state: SignInCardState }) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
  const refused = state === 'refused'

  return (
    <Flex vertical align="center" gap={spacing.md} style={{ paddingBlock: spacing.xl }}>
      <OrganizationHeading />
      <Card
        data-testid="sign-in-card"
        style={{
          width: '100%',
          maxWidth: 400,
          border: `1px solid ${colour.borderSubtle}`,
          borderRadius: radius.base,
        }}
        styles={{ body: { padding: spacing.xl } }}
      >
        <Flex vertical gap={spacing.md}>
          <Typography.Title level={4} style={{ margin: 0 }}>
            {t(refused ? 'shell.home.signIn.title' : 'shell.home.signedOut.title')}
          </Typography.Title>
          {refused ? (
            <Alert
              data-testid="sign-in-failed"
              role="alert"
              type="warning"
              showIcon
              title={t('shell.home.signInFailed')}
            />
          ) : (
            <Typography.Paragraph style={{ margin: 0, color: colour.textMuted }}>
              {t('shell.home.signedOut.message')}
            </Typography.Paragraph>
          )}
          <Button type="primary" block onClick={startSignIn}>
            {t(refused ? 'shell.account.signIn' : 'shell.home.signedOut.signIn')}
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
      data-home-tile
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
        <Typography.Text strong style={{ color: colour.primaryText }}>
          {title}
        </Typography.Text>
        <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>
      </Flex>
    </Link>
  )
}

function Tools({ user }: { user: CurrentUser }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const grid = useHomeGrid()
  if (!user.studio && !user.console && !user.users) {
    return null
  }
  return (
    <Flex vertical gap={spacing.md}>
      <Typography.Title level={5} style={{ margin: 0 }}>
        {t('shell.home.tools.title')}
      </Typography.Title>
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
    </Flex>
  )
}

function ApplicationCard({
  title,
  description,
  sites,
  locale,
}: {
  title: string
  description: string | null
  sites: SiteListItem[]
  locale: string
}) {
  const { colour, radius, spacing } = useDesignTokens()
  const grid = useHomeGrid()
  const headingId = useId()
  const sorted = [...sites].sort((a, b) => siteTitle(a, locale).localeCompare(siteTitle(b, locale), locale))

  return (
    <section
      aria-labelledby={headingId}
      style={{
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        padding: spacing.md,
      }}
    >
      <Flex
        data-testid="application-card-header"
        align={description === null ? 'center' : 'flex-start'}
        gap={spacing.sm}
      >
        <SiteMark title={title} size={32} />
        <Flex vertical>
          <Typography.Title level={5} id={headingId} style={{ margin: 0 }}>
            {title}
          </Typography.Title>
          {description !== null && <Typography.Text style={{ color: colour.textMuted }}>{description}</Typography.Text>}
        </Flex>
      </Flex>
      <div style={{ borderTop: `1px solid ${colour.divider}`, marginBlock: spacing.md }} />
      <div data-testid="home-grid" style={grid}>
        {sorted.map((site) => {
          const siteDescription = localized(site.descriptions, locale)
          return (
            <Link
              key={site.path}
              to={siteAddress(site.path)}
              data-home-tile
              style={{
                display: 'flex',
                alignItems: siteDescription === null ? 'center' : 'flex-start',
                gap: spacing.sm,
                minWidth: 0,
              }}
            >
              <SiteMark title={siteTitle(site, locale)} size={20} />
              <Flex vertical style={{ minWidth: 0 }}>
                <Typography.Text strong style={{ color: colour.primaryText }}>
                  {siteTitle(site, locale)}
                </Typography.Text>
                {siteDescription !== null && (
                  <Typography.Text style={{ color: colour.textMuted }}>{siteDescription}</Typography.Text>
                )}
              </Flex>
            </Link>
          )
        })}
      </div>
    </section>
  )
}

function Applications({ sites, locale }: { sites: SiteListItem[] | 'failed'; locale: string }) {
  const t = useText()
  const { spacing } = useDesignTokens()
  const groups = useMemo(() => {
    if (sites === 'failed') {
      return []
    }
    const byName = new Map<string, { title: string; description: string | null; sites: SiteListItem[] }>()
    for (const site of sites) {
      const group = byName.get(site.application.name)
      if (group) {
        group.sites.push(site)
      } else {
        byName.set(site.application.name, {
          title: applicationTitle(site.application, locale),
          description: localized(site.application.descriptions, locale),
          sites: [site],
        })
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
      <ApplicationCard
        key={group.name}
        title={group.title}
        description={group.description}
        sites={group.sites}
        locale={locale}
      />
    ))
  }

  return (
    <Flex vertical gap={spacing.md}>
      <Typography.Title level={5} style={{ margin: 0 }}>
        {t('shell.home.applications.title')}
      </Typography.Title>
      {body}
    </Flex>
  )
}

export function HomePage() {
  const { locale } = usePlatform().current
  const { spacing } = useDesignTokens()
  const [loaded, setLoaded] = useState<Loaded>()
  const { pathname, search } = useLocation()
  // The card is the one the address asked for when the page opened.
  const [card] = useState(() => cardOfAddress(pathname, search))

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
      const loadedUser = user.status === 'fulfilled' ? user.value : 'failed'
      const loadedSites = sites.status === 'fulfilled' ? sites.value : 'failed'
      // Either call answering 401 means there is no session. Without a card address, sign-in starts at once
      // and the spinner stays until the browser leaves.
      if ((loadedUser === null || loadedSites === null) && card === null) {
        startSignIn()
        return
      }
      setLoaded({ user: loadedUser, sites: loadedSites, card })
    })
    return () => controller.abort()
  }, [card])

  if (loaded === undefined) {
    return <Spin />
  }
  // Either call answering 401 means the session is gone, even if the other call went through before it ended.
  if (loaded.user === null || loaded.sites === null) {
    return loaded.card === null ? <Spin /> : <SignInCard state={loaded.card} />
  }

  return (
    <Flex vertical gap={spacing.lg}>
      <HomeTileStyle />
      <Applications sites={loaded.sites} locale={locale} />
      {loaded.user !== 'failed' && <Tools user={loaded.user} />}
    </Flex>
  )
}
