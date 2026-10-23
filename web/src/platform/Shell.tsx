import { Button, ConfigProvider, Flex, Layout, Menu, Segmented, Space, Switch, Typography } from 'antd'
import dayjs from 'dayjs'
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import type { NavigationItem } from './site'
import { useText } from './texts'
import { useDesignTokens, useThemeMode } from './themeMode'
import { UserMenu } from './UserMenu'
import { usePhoneWidth } from './usePhoneWidth'

interface ShellProps {
  titleKey: string
  navigation: readonly NavigationItem[]
  locales: readonly string[]
  locale: string
  onLocaleChange: (locale: string) => void
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
 * The layout of every site, the platform site and application sites alike: site title and
 * navigation, account button, locale switch and theme toggle. Ant Design components and dayjs follow the
 * locale. Below the phone breakpoint the navigation, locale switch and theme toggle sit in a panel that
 * a Menu button opens and closes.
 */
export function Shell({ titleKey, navigation, locales, locale, onLocaleChange, children }: ShellProps) {
  const t = useText()
  const { mode, toggleMode } = useThemeMode()
  const { pathname } = useLocation()
  const { colour, spacing } = useDesignTokens()
  const activeKey = activeNavigationKey(navigation, pathname)
  const phone = usePhoneWidth()
  // The panel is open for the path it was opened on, so following a link or leaving the phone layout closes it.
  const [openOn, setOpenOn] = useState<{ pathname: string; phone: boolean } | null>(null)
  const open = phone && openOn?.pathname === pathname && openOn.phone
  const toggle = useRef<HTMLButtonElement>(null)
  const items = navigation.map((item) => ({
    key: item.key,
    label: (
      <Link to={item.path} aria-current={item.key === activeKey ? 'page' : undefined}>
        {t(item.labelKey)}
      </Link>
    ),
  }))
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

  const setOpen = (next: boolean) => setOpenOn(next ? { pathname, phone } : null)

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
          {phone ? (
            <>
              <Layout.Header
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: spacing.sm,
                  paddingInline: spacing.lg,
                  background: colour.surface,
                  borderBottom: `1px solid ${colour.borderSubtle}`,
                }}
              >
                <div style={{ flex: 1, minWidth: 0 }}>
                  <Typography.Text strong ellipsis>
                    {t(titleKey)}
                  </Typography.Text>
                </div>
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
              </Layout.Header>
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
            </>
          ) : (
            <Layout.Header
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: spacing.lg,
                paddingInline: spacing.lg,
                background: colour.surface,
                borderBottom: `1px solid ${colour.borderSubtle}`,
              }}
            >
              <Typography.Text strong>{t(titleKey)}</Typography.Text>
              <Menu
                mode="horizontal"
                style={{ flex: 1, minWidth: 0, borderBottom: 'none' }}
                selectedKeys={activeKey ? [activeKey] : []}
                items={items}
              />
              <Space>
                <UserMenu />
                {localeSwitch}
                {themeSwitch}
              </Space>
            </Layout.Header>
          )}
          <Layout.Content style={{ padding: spacing.lg }}>{children}</Layout.Content>
        </Layout>
      </ConfigProvider>
    </div>
  )
}
