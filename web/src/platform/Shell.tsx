import { MenuFoldOutlined, MenuOutlined, MenuUnfoldOutlined, SettingOutlined } from '@ant-design/icons'
import { Avatar, ConfigProvider, Dropdown, Layout, Menu, Typography, type MenuProps } from 'antd'
import dayjs from 'dayjs'
import { useContext, useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import { AxisButton } from './AxisButton'
import { navigationIcon } from './navigationIcons'
import { NotificationBell } from './NotificationBell'
import { ShellHeightContext } from './shellHeight'
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
  /** Whether the sidebar starts collapsed, as a design-system preview draws it. It starts open otherwise. */
  initiallyCollapsed?: boolean
  children: ReactNode
}

// Proposed: the header is 48 px high, and the navigation sits in a sidebar of 224 px that collapses to a strip
// of icons 64 px wide.
const headerHeight = 48
const sidebarWidth = 224
const collapsedWidth = 64

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
 * Proposed: the site's mark, the first letter of its title on a `primary` tile, beside the title in the header. The
 * avatar draws its letter in the light solid text colour, which reads on `primary` in both modes.
 */
function SiteMark({ title }: { title: string }) {
  const { colour } = useDesignTokens()
  return (
    <Avatar aria-hidden shape="square" size={28} style={{ flex: 'none', background: colour.primary, fontWeight: 600 }}>
      {title.trim().charAt(0).toUpperCase()}
    </Avatar>
  )
}

/**
 * Proposed: the language and the theme sit in one menu behind an icon button in the header, for signed-in
 * users and for nobody signed in alike, in place of the locale switch and the theme toggle.
 */
function PreferencesMenu({
  locales,
  locale,
  onLocaleChange,
}: Pick<ShellProps, 'locales' | 'locale' | 'onLocaleChange'>) {
  const t = useText()
  const { mode, toggleMode } = useThemeMode()
  const items: MenuProps['items'] = [
    {
      type: 'group',
      label: t('shell.locale.label'),
      children: locales.map((tag) => ({ key: `locale:${tag}`, label: t(`shell.locale.${tag}`) })),
    },
    { type: 'divider' },
    {
      type: 'group',
      label: t('shell.theme.label'),
      children: [
        { key: 'theme:light', label: t('shell.theme.lightShort') },
        { key: 'theme:dark', label: t('shell.theme.darkShort') },
      ],
    },
  ]
  const onClick: MenuProps['onClick'] = ({ key }) => {
    const [kind, value] = key.split(':')
    if (kind === 'locale' && value !== locale) {
      onLocaleChange(value)
    } else if (kind === 'theme' && value !== mode) {
      toggleMode()
    }
  }
  return (
    <Dropdown
      trigger={['click']}
      menu={{
        items,
        onClick,
        selectable: true,
        selectedKeys: [`locale:${locale}`, `theme:${mode}`],
        'aria-label': t('shell.preferences.label'),
      }}
    >
      <span style={{ display: 'inline-flex' }}>
        <AxisButton role="icon" nameKey="shell.preferences.label" icon={<SettingOutlined />} aria-haspopup="menu" />
      </span>
    </Dropdown>
  )
}

/**
 * The layout of every site, the platform site and application sites alike: a header with the site's mark and
 * title, the notification bell, the language and theme menu and the account button, and the site's navigation in
 * a sidebar beside the page. The sidebar collapses to a strip of the entries' icons, each named by a tooltip. Ant
 * Design components and dayjs follow the locale. At narrow width, below 992 px, the navigation sits in a panel
 * under the header, which a Menu button opens and closes.
 */
export function Shell({
  titleKey,
  navigation,
  locales,
  locale,
  onLocaleChange,
  notifications,
  initiallyCollapsed = false,
  children,
}: ShellProps) {
  const t = useText()
  const { mode } = useThemeMode()
  const { pathname } = useLocation()
  const { colour, spacing } = useDesignTokens()
  const activeKey = activeNavigationKey(navigation, pathname)
  const narrow = useNarrowWidth()
  const minHeight = useContext(ShellHeightContext)
  // Proposed: the sidebar stays open or collapsed while the user moves between pages.
  const [collapsed, setCollapsed] = useState(initiallyCollapsed)
  // The panel is open for the path it was opened on, so following a link or leaving narrow width closes it.
  const [openOn, setOpenOn] = useState<{ pathname: string; narrow: boolean } | null>(null)
  const open = narrow && openOn?.pathname === pathname && openOn.narrow
  // The Menu button's wrapper, so Escape can give the button its focus back.
  const toggle = useRef<HTMLSpanElement>(null)
  const title = t(titleKey)
  const items = navigation.map((item) => ({
    key: item.key,
    icon: navigationIcon(item.icon),
    label: (
      <Link to={item.path} aria-current={item.key === activeKey ? 'page' : undefined}>
        {t(item.labelKey)}
      </Link>
    ),
  }))
  const menu = (
    <Menu mode="inline" style={{ borderInlineEnd: 'none' }} selectedKeys={activeKey ? [activeKey] : []} items={items} />
  )

  const setOpen = (next: boolean) => setOpenOn(next ? { pathname, narrow } : null)

  function closeOnEscape(event: KeyboardEvent) {
    if (event.key === 'Escape' && open) {
      setOpen(false)
      toggle.current?.querySelector('button')?.focus()
    }
  }

  useEffect(() => {
    dayjs.locale(dayjsLocaleFor(locale))
  }, [locale])

  const navigationToggle = narrow ? (
    <span ref={toggle} style={{ display: 'inline-flex' }}>
      <AxisButton
        role="icon"
        nameKey="shell.navigation.menu"
        icon={<MenuOutlined />}
        aria-expanded={open}
        aria-controls="shell-menu"
        onClick={() => setOpen(!open)}
        onKeyDown={closeOnEscape}
      />
    </span>
  ) : (
    <AxisButton
      role="icon"
      nameKey={collapsed ? 'shell.navigation.expand' : 'shell.navigation.collapse'}
      icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
      aria-expanded={!collapsed}
      aria-controls="shell-navigation"
      onClick={() => setCollapsed(!collapsed)}
    />
  )

  // This provider sets only the locale. It inherits the theme from the provider in theme.tsx.
  return (
    <div data-theme-mode={mode}>
      <ConfigProvider locale={antLocaleFor(locale)}>
        <Layout style={{ minHeight }}>
          <Layout.Header
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: spacing.sm,
              height: headerHeight,
              lineHeight: `${headerHeight}px`,
              paddingInline: spacing.md,
              background: colour.surface,
              borderBottom: `1px solid ${colour.borderSubtle}`,
            }}
          >
            {navigationToggle}
            <SiteMark title={title} />
            <div style={{ flex: 1, minWidth: 0 }}>
              <Typography.Text strong ellipsis>
                {title}
              </Typography.Text>
            </div>
            {notifications && (
              <NotificationBell path={notifications.path} sitePath={notifications.sitePath} locale={locale} />
            )}
            <PreferencesMenu locales={locales} locale={locale} onLocaleChange={onLocaleChange} />
            <UserMenu />
          </Layout.Header>
          {narrow ? (
            <>
              <nav
                id="shell-menu"
                aria-label={t('shell.navigation.menu')}
                hidden={!open}
                onKeyDown={closeOnEscape}
                style={{
                  padding: spacing.sm,
                  background: colour.surface,
                  borderBottom: `1px solid ${colour.borderSubtle}`,
                }}
              >
                {menu}
              </nav>
              <Layout.Content style={{ padding: spacing.lg }}>{children}</Layout.Content>
            </>
          ) : (
            <Layout hasSider>
              <Layout.Sider
                id="shell-navigation"
                width={sidebarWidth}
                collapsedWidth={collapsedWidth}
                collapsed={collapsed}
                trigger={null}
                theme="light"
                style={{ background: colour.surface, borderInlineEnd: `1px solid ${colour.borderSubtle}` }}
              >
                <nav aria-label={t('shell.navigation.label')} style={{ paddingBlock: spacing.xs }}>
                  {menu}
                </nav>
              </Layout.Sider>
              <Layout.Content style={{ padding: spacing.lg, minWidth: 0 }}>{children}</Layout.Content>
            </Layout>
          )}
        </Layout>
      </ConfigProvider>
    </div>
  )
}
