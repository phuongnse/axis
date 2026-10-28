import { MenuFoldOutlined, MenuUnfoldOutlined } from '@ant-design/icons'
import { Button, ConfigProvider, Flex, Layout, Menu, Segmented, Space, Switch, Typography } from 'antd'
import dayjs from 'dayjs'
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import { colourTokens } from './designTokens'
import { navigationIcon } from './navigationIcons'
import { NotificationBell } from './NotificationBell'
import { ShellFooter } from './ShellFooter'
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
 * collapse button, the site's mark and title, the notification bell, the account button, the locale
 * switch and the theme toggle. A site with two or more navigation entries has a sidebar of icons and
 * labels that collapses to a strip of icons. A footer runs across the bottom. Ant Design components
 * and dayjs follow the locale. At narrow width, below 992 px, the navigation, locale switch and
 * theme toggle sit in a panel that a Menu button opens and closes, and there is no sidebar.
 */
export function Shell({ titleKey, navigation, locales, locale, onLocaleChange, notifications, children }: ShellProps) {
  const t = useText()
  const { mode, toggleMode } = useThemeMode()
  const { pathname } = useLocation()
  const { colour, spacing, radius } = useDesignTokens()
  const activeKey = activeNavigationKey(navigation, pathname)
  const narrow = useNarrowWidth()
  // The panel is open for the path it was opened on, so following a link or leaving narrow width closes it.
  const [openOn, setOpenOn] = useState<{ pathname: string; narrow: boolean } | null>(null)
  const open = narrow && openOn?.pathname === pathname && openOn.narrow
  const toggle = useRef<HTMLButtonElement>(null)
  // The sidebar stays collapsed or open while the person moves between pages, as the shell stays mounted.
  const [collapsed, setCollapsed] = useState(false)
  const hasSidebar = navigation.length > 1
  const sidebar = hasSidebar && !narrow
  const home = navigation[0]?.path ?? '/'
  const items = navigation.map((item) => {
    const Icon = navigationIcon(item.icon)
    return {
      key: item.key,
      icon: <Icon aria-hidden />,
      label: (
        <Link to={item.path} aria-current={item.key === activeKey ? 'page' : undefined}>
          {t(item.labelKey)}
        </Link>
      ),
    }
  })
  const localeSwitch = (
    <Segmented
      aria-label={t('shell.locale.label')}
      value={locale}
      options={locales.map((tag) => ({
        value: tag,
        label: t(`shell.locale.${tag}`),
      }))}
      onChange={onLocaleChange}
    />
  )
  const themeSwitch = (
    <Switch
      aria-label={t('shell.theme.dark')}
      checked={mode === 'dark'}
      checkedChildren={t('shell.theme.darkShort')}
      unCheckedChildren={t('shell.theme.lightShort')}
      onChange={toggleMode}
    />
  )

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
        <Layout style={{ minHeight: '100vh' }}>
          <Layout.Header
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: spacing.sm,
              height: 48,
              lineHeight: 'normal',
              paddingInline: spacing.lg,
              background: colour.surface,
              borderBottom: `1px solid ${colour.borderSubtle}`,
            }}
          >
            {sidebar && (
              <Button
                type="text"
                icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
                aria-label={t(collapsed ? 'shell.navigation.expand' : 'shell.navigation.collapse')}
                aria-expanded={!collapsed}
                aria-controls="shell-sidebar"
                onClick={() => setCollapsed(!collapsed)}
              />
            )}
            <Link to={home} style={{ display: 'flex', alignItems: 'center', gap: spacing.sm, minWidth: 0 }}>
              <span
                aria-hidden
                style={{
                  display: 'grid',
                  placeItems: 'center',
                  flex: 'none',
                  width: 28,
                  height: 28,
                  background: colour.primary,
                  // White in both modes, as the tile is the same purple family in both.
                  color: colourTokens.surface.light,
                  fontWeight: 600,
                  borderRadius: radius.base,
                }}
              >
                {Array.from(t(titleKey))[0]?.toUpperCase()}
              </span>
              <Typography.Text strong ellipsis>
                {t(titleKey)}
              </Typography.Text>
            </Link>
            <div style={{ flex: 1 }} />
            {narrow ? (
              <>
                {bell}
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
              <Space>
                {bell}
                <UserMenu />
                {localeSwitch}
                {themeSwitch}
              </Space>
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
              <Menu
                mode="inline"
                style={{ borderInlineEnd: 'none' }}
                selectedKeys={activeKey ? [activeKey] : []}
                items={items}
              />
              <Flex vertical gap={spacing.md} align="flex-start">
                {localeSwitch}
                {themeSwitch}
              </Flex>
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
                <nav aria-label={t('shell.navigation.menu')}>
                  <Menu
                    mode="inline"
                    style={{ borderInlineEnd: 'none' }}
                    selectedKeys={activeKey ? [activeKey] : []}
                    items={items}
                  />
                </nav>
              </Layout.Sider>
            )}
            <Layout.Content style={{ padding: spacing.lg }}>{children}</Layout.Content>
          </Layout>
          <ShellFooter />
        </Layout>
      </ConfigProvider>
    </div>
  )
}
