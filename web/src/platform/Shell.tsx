import { MenuFoldOutlined, MenuUnfoldOutlined } from '@ant-design/icons'
import { Button, ConfigProvider, Flex, Layout, Typography, type ThemeConfig } from 'antd'
import dayjs from 'dayjs'
import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { AppSwitcher } from './AppSwitcher'
import { AxisButton } from './AxisButton'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import { NotificationBell } from './NotificationBell'
import { ReadOnlyBanner } from './ReadOnlyBanner'
import { PreferencesMenu } from './PreferencesMenu'
import { sizeTokens } from './designTokens'
import { ShellFooter } from './ShellFooter'
import { ShellNavigation } from './ShellNavigation'
import { ShellAsideContext } from './shellAsideContext'
import { ShellSidebarContext } from './shellSidebarContext'
import { SiteMark } from './SiteMark'
import type { NavigationItem, SiteLegal } from './site'
import { useText } from './texts'
import { useDesignTokens, useThemeMode } from './themeMode'
import { UserMenu } from './UserMenu'
import { useNarrowWidth } from './useNarrowWidth'

const headerHeight = sizeTokens.bar

// Never undefined: Ant Design wraps its children only when a theme is given, so a switch would remount the page.
const claimedTheme: ThemeConfig = { components: { Menu: { itemHeight: 32 } } }
const siteTheme: ThemeConfig = {}

interface ShellProps {
  titleKey: string
  navigation: readonly NavigationItem[]
  locales: readonly string[]
  locale: string
  onLocaleChange: (locale: string) => void
  /** Where the notification bell reads, set on an application site and, for an administrator, on the platform site. Without it the header has no bell. */
  notifications?: { path: string; sitePath: string }
  /** The privacy and terms links the footer shows, from the site the shell shows. */
  legal?: SiteLegal | null
  /** What the header shows in place of the app switcher and the account button, such as the cell console's operator name. `null` shows nothing there. */
  account?: ReactNode
  children: ReactNode
}

/**
 * The navigation entry the path belongs to: the one whose path equals it or is a parent of it, and
 * the longest such path when several are. Site paths are matched ignoring letter case, as the server
 * does. Home (`/`) belongs only to `/` itself.
 */
function activeNavigationKey(navigation: readonly NavigationItem[], pathname: string): string | undefined {
  const current = pathname.toLowerCase()
  let best: NavigationItem | undefined
  for (const item of navigation) {
    const path = item.path.toLowerCase()
    const matches = path === '/' ? current === '/' : current === path || current.startsWith(`${path}/`)
    if (matches && (best === undefined || path.length > best.path.length)) {
      best = item
    }
  }
  return best?.key
}

/**
 * The layout of every site, the platform site and application sites alike. A 48 px header holds the
 * collapse button, the site's mark and title, the notification bell, the app switcher, the language
 * and theme menu and the account button. The page keeps the browser's scroll, while the header stays at the top
 * and the sidebar's navigation stays under it with a scroll of its own. A site with two or more navigation entries
 * has a sidebar of labels that collapses to a strip of icons. A page can claim the sidebar with `ShellSidebar` and
 * draw its own navigation there, even on a one-page site, and then the page gets the tighter workspace padding.
 * Proposed: a page without a sidebar has a footer across the bottom, with the copyright at the start and the legal
 * links at the end. A page with a sidebar has none, so the sidebar and a side panel reach the bottom of the window, and
 * the account menu holds the legal line. A page can open a side panel in a right column with `ShellAside`.
 * Ant Design components and dayjs follow the locale. At narrow width, below
 * 992 px, the navigation and the language and theme menu sit in a panel that a Menu button opens and closes, and
 * there is no sidebar. The app switcher stays in the header. A shell given `account` shows it in place of the app switcher
 * and the account button. While the tenant is read-only, a banner tops the content
 * column on every page, see `ReadOnlyBanner`. A shell given `account` has no tenant and shows no banner.
 */
export function Shell({
  titleKey,
  navigation,
  locales,
  locale,
  onLocaleChange,
  notifications,
  legal,
  account,
  children,
}: ShellProps) {
  const t = useText()
  const { mode } = useThemeMode()
  const { pathname } = useLocation()
  const { colour, spacing } = useDesignTokens()
  const activeKey = activeNavigationKey(navigation, pathname)
  const narrow = useNarrowWidth()
  // The panel is open for the path it was opened on, so following a link or leaving narrow width closes it.
  const [openOn, setOpenOn] = useState<{ pathname: string; narrow: boolean } | null>(null)
  const open = narrow && openOn?.pathname === pathname && openOn.narrow
  const toggle = useRef<HTMLButtonElement>(null)
  // The sidebar stays collapsed or open while the person moves between pages, as the shell stays mounted.
  const [collapsed, setCollapsed] = useState(false)
  // A page claims the sidebar by rendering ShellSidebar, which draws its content into `target`.
  const [claims, setClaims] = useState(0)
  const [target, setTarget] = useState<HTMLElement | null>(null)
  const claim = useCallback(() => {
    setClaims((count) => count + 1)
    return () => setClaims((count) => count - 1)
  }, [])
  const claimed = claims > 0
  // Proposed: a page claims the right column by rendering ShellAside, which draws its side panel into `asideTarget`.
  const [asideClaims, setAsideClaims] = useState(0)
  const [asideTarget, setAsideTarget] = useState<HTMLElement | null>(null)
  const claimAside = useCallback(() => {
    setAsideClaims((count) => count + 1)
    return () => setAsideClaims((count) => count - 1)
  }, [])
  const asideContext = useMemo(() => ({ target: asideTarget, claim: claimAside }), [asideTarget, claimAside])
  const sidebarContext = useMemo(
    () => ({ target, collapsed: collapsed && !narrow, claim }),
    [target, collapsed, narrow, claim],
  )
  const hasSidebar = claimed || navigation.length > 1
  const sidebar = hasSidebar && !narrow
  const home = navigation[0]?.path ?? '/'
  const entries = navigation.map((item) => ({ ...item, label: t(item.labelKey) }))
  // The page's own navigation, or the site's.
  const navigationContent = (iconsOnly: boolean) =>
    claimed ? (
      <div ref={setTarget} />
    ) : (
      <ShellNavigation entries={entries} activeKey={activeKey} collapsed={iconsOnly} />
    )
  const hasAccount = account !== undefined
  const preferences = <PreferencesMenu locales={locales} locale={locale} onLocaleChange={onLocaleChange} />

  const bell = notifications && (
    <NotificationBell path={notifications.path} sitePath={notifications.sitePath} locale={locale} />
  )

  const setOpen = (next: boolean) => setOpenOn(next ? { pathname, narrow } : null)

  function closeOnEscape(event: KeyboardEvent) {
    if (event.key === 'Escape' && open) {
      setOpen(false)
      toggle.current?.focus()
    }
  }

  useEffect(() => {
    dayjs.locale(dayjsLocaleFor(locale))
  }, [locale])

  // This provider sets the locale, and the tighter menu items while a page claims the sidebar, as the page draws its
  // own navigation there. It inherits the rest of the theme from the provider in theme.tsx.
  return (
    <div data-theme-mode={mode} style={{ colorScheme: mode }}>
      <ConfigProvider locale={antLocaleFor(locale)} theme={claimed ? claimedTheme : siteTheme}>
        {/* A design-system frame sets --shell-min-height to its own height, so its states fit their card. */}
        <Layout style={{ minHeight: 'var(--shell-min-height, 100vh)' }}>
          <Layout.Header
            style={{
              position: 'sticky',
              top: 0,
              zIndex: 10,
              display: 'flex',
              alignItems: 'center',
              gap: spacing.sm,
              height: headerHeight,
              lineHeight: 'normal',
              paddingInline: spacing.md,
              background: colour.surface,
              borderBottom: `1px solid ${colour.borderSubtle}`,
            }}
          >
            {sidebar && (
              <AxisButton
                role="icon"
                nameKey={collapsed ? 'shell.navigation.expand' : 'shell.navigation.collapse'}
                icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
                aria-expanded={!collapsed}
                aria-controls="shell-sidebar"
                onClick={() => setCollapsed(!collapsed)}
              />
            )}
            <Link to={home} style={{ display: 'flex', alignItems: 'center', gap: spacing.sm, minWidth: 0 }}>
              <SiteMark title={t(titleKey)} />
              <Typography.Text strong ellipsis>
                {t(titleKey)}
              </Typography.Text>
            </Link>
            <div style={{ flex: 1 }} />
            {narrow ? (
              <>
                {bell}
                {hasAccount ? account : <AppSwitcher locale={locale} />}
                {!hasAccount && <UserMenu />}
                {/* oxlint-disable-next-line axis/no-antd-button -- Predates AxisButton; move it to AxisButton with a role. */}
                <Button
                  ref={toggle}
                  type="text"
                  aria-expanded={open}
                  aria-controls="shell-menu"
                  onClick={() => setOpen(!open)}
                  onKeyDown={closeOnEscape}
                >
                  {t('shell.navigation.menu')}
                </Button>
              </>
            ) : (
              <Flex align="center" gap={spacing.sm}>
                {bell}
                {!hasAccount && <AppSwitcher locale={locale} />}
                {preferences}
                {hasAccount ? account : <UserMenu legal={legal} />}
              </Flex>
            )}
          </Layout.Header>
          {narrow && (
            <nav
              id="shell-menu"
              aria-label={t('shell.navigation.menu')}
              hidden={!open}
              onKeyDown={closeOnEscape}
              style={{
                padding: spacing.lg,
                background: colour.surface,
                borderBottom: `1px solid ${colour.borderSubtle}`,
              }}
            >
              {navigationContent(false)}
              {preferences}
            </nav>
          )}
          <Layout hasSider={sidebar}>
            {sidebar && (
              <Layout.Sider
                id="shell-sidebar"
                width={224}
                collapsedWidth={64}
                collapsible
                collapsed={collapsed}
                trigger={null}
                style={{ background: colour.surface, borderInlineEnd: `1px solid ${colour.borderSubtle}` }}
              >
                <nav
                  aria-label={t('shell.navigation.menu')}
                  style={{
                    position: 'sticky',
                    top: headerHeight,
                    maxHeight: `calc(var(--shell-min-height, 100vh) - ${headerHeight}px)`,
                    overflowY: 'auto',
                  }}
                >
                  {navigationContent(collapsed)}
                </nav>
              </Layout.Sider>
            )}
            <Layout>
              {!hasAccount && <ReadOnlyBanner />}
              <Layout.Content style={{ padding: claimed ? spacing.md : spacing.lg }}>
                <ShellSidebarContext.Provider value={sidebarContext}>
                  <ShellAsideContext.Provider value={asideContext}>{children}</ShellAsideContext.Provider>
                </ShellSidebarContext.Provider>
              </Layout.Content>
            </Layout>
            {asideClaims > 0 && (
              // Proposed: the right column, as tall as the page, with its side panel under the header to the window's bottom.
              <div
                style={{
                  flex: 'none',
                  width: sizeTokens.sidePanel,
                  background: colour.surface,
                  borderInlineStart: `1px solid ${colour.borderSubtle}`,
                }}
              >
                <div
                  ref={setAsideTarget}
                  style={{
                    position: 'sticky',
                    top: headerHeight,
                    height: `calc(100vh - ${headerHeight}px)`,
                    display: 'flex',
                    flexDirection: 'column',
                  }}
                />
              </div>
            )}
          </Layout>
          {/* Proposed: a page with a sidebar has no footer; the legal line is in the account menu. */}
          {!sidebar && <ShellFooter legal={legal} />}
        </Layout>
      </ConfigProvider>
    </div>
  )
}
