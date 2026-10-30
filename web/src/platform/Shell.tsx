import { MenuFoldOutlined, MenuUnfoldOutlined } from '@ant-design/icons'
import { Button, ConfigProvider, Flex, Layout, Menu, Typography } from 'antd'
import dayjs from 'dayjs'
import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { AppSwitcher } from './AppSwitcher'
import { AxisButton } from './AxisButton'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import { navigationIcon } from './navigationIcons'
import { NotificationBell } from './NotificationBell'
import { PreferencesMenu } from './PreferencesMenu'
import { ShellFooter } from './ShellFooter'
import { ShellSidebarContext } from './shellSidebar'
import { SiteMark } from './SiteMark'
import type { NavigationItem } from './site'
import { useText } from './texts'
import { useDesignTokens, useThemeMode } from './themeMode'
import { UserMenu } from './UserMenu'
import { useNarrowWidth } from './useNarrowWidth'

interface ShellProps {
  titleKey: string
  navigation: readonly NavigationItem[]
  locales: readonly string[]
  locale: string
  onLocaleChange: (locale: string) => void
  /** Where the notification bell reads, set on an application site. Without it the header has no bell. */
  notifications?: { path: string; sitePath: string }
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
 * and theme menu and the account button. A site with two or more navigation entries has a sidebar of icons and
 * labels that collapses to a strip of icons. A footer runs across the bottom. Ant Design components
 * and dayjs follow the locale. At narrow width, below 992 px, the navigation and the language and
 * theme menu sit in a panel that a Menu button opens and closes, and there is no sidebar. The app
 * switcher stays in the header.
 */
export function Shell({ titleKey, navigation, locales, locale, onLocaleChange, notifications, children }: ShellProps) {
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
  // Proposed: a page with its own navigation claims the sidebar and draws in `pageSidebarSlot`.
  const [pageSidebar, setPageSidebar] = useState(false)
  const [pageSidebarSlot, setPageSidebarSlot] = useState<HTMLElement | null>(null)
  // Proposed: a page with its own navigation is a workspace, drawn compact: spacing-md round its content.
  const contentPadding = pageSidebar ? spacing.md : spacing.lg
  const sidebarContext = useMemo(
    () => ({ slot: pageSidebarSlot, collapsed: collapsed && !narrow, claim: setPageSidebar, contentPadding }),
    [pageSidebarSlot, collapsed, narrow, contentPadding],
  )
  const hasSidebar = navigation.length > 1 || pageSidebar
  const sidebar = hasSidebar && !narrow
  const home = navigation[0]?.path ?? '/'
  // Proposed: an entry is its label while the sidebar is open and its icon, centred, while it is collapsed, where a
  // tooltip names it, as an action has text or an icon, never both.
  const items = navigation.map((item) => {
    const Icon = navigationIcon(item.icon)
    return {
      key: item.key,
      icon: collapsed && !narrow ? <Icon aria-hidden /> : undefined,
      label: (
        <Link to={item.path} aria-current={item.key === activeKey ? 'page' : undefined}>
          {t(item.labelKey)}
        </Link>
      ),
    }
  })
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

  // This provider sets only the locale. It inherits the theme from the provider in theme.tsx.
  return (
    <div data-theme-mode={mode}>
      <ConfigProvider locale={antLocaleFor(locale)}>
        {/* A design-system frame sets --shell-min-height to its own height, so its states fit their card. */}
        <Layout style={{ minHeight: 'var(--shell-min-height, 100vh)' }}>
          <Layout.Header
            style={{
              // Proposed: the header stays at the top while the page scrolls under it.
              position: 'sticky',
              top: 0,
              zIndex: 10,
              display: 'flex',
              alignItems: 'center',
              gap: spacing.sm,
              height: 48,
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
                <AppSwitcher locale={locale} />
                <UserMenu />
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
                <AppSwitcher locale={locale} />
                {preferences}
                <UserMenu />
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
              {pageSidebar ? (
                <div ref={setPageSidebarSlot} />
              ) : (
                <Menu
                  mode="inline"
                  style={{ borderInlineEnd: 'none' }}
                  selectedKeys={activeKey ? [activeKey] : []}
                  items={items}
                />
              )}
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
                {/* Proposed: the sidebar's background runs the page's height, and its navigation stays under the header
                    while the page scrolls, with a scroll of its own when it is taller than the window. */}
                <div
                  style={{
                    position: 'sticky',
                    top: 48,
                    maxHeight: 'calc(100vh - 48px)',
                    overflowY: 'auto',
                    // Proposed: its scrollbar is the page's own, as the colour scheme draws it, so the two match.
                  }}
                >
                  {pageSidebar ? (
                    <div ref={setPageSidebarSlot} />
                  ) : (
                    <nav aria-label={t('shell.navigation.menu')}>
                      <Menu
                        mode="inline"
                        style={{ borderInlineEnd: 'none' }}
                        selectedKeys={activeKey ? [activeKey] : []}
                        items={items}
                      />
                    </nav>
                  )}
                </div>
              </Layout.Sider>
            )}
            <Layout.Content style={{ padding: contentPadding }}>
              <ShellSidebarContext.Provider value={sidebarContext}>{children}</ShellSidebarContext.Provider>
            </Layout.Content>
          </Layout>
          <ShellFooter />
        </Layout>
      </ConfigProvider>
    </div>
  )
}
