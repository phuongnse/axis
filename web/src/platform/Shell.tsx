import { ConfigProvider, Layout, Menu, Segmented, Space, Switch, Typography } from 'antd'
import dayjs from 'dayjs'
import { useEffect, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import type { NavigationItem } from './site'
import { useText } from './texts'
import { useDesignTokens, useThemeMode } from './themeMode'
import { UserMenu } from './UserMenu'

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
 * locale.
 */
export function Shell({ titleKey, navigation, locales, locale, onLocaleChange, children }: ShellProps) {
  const t = useText()
  const { mode, toggleMode } = useThemeMode()
  const { pathname } = useLocation()
  const { colour, spacing } = useDesignTokens()
  const activeKey = activeNavigationKey(navigation, pathname)

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
              items={navigation.map((item) => ({
                key: item.key,
                label: (
                  <Link to={item.path} aria-current={item.key === activeKey ? 'page' : undefined}>
                    {t(item.labelKey)}
                  </Link>
                ),
              }))}
            />
            <Space>
              <UserMenu />
              <Segmented
                aria-label={t('shell.locale.label')}
                value={locale}
                options={locales.map((tag) => ({
                  value: tag,
                  label: t(`shell.locale.${tag}`),
                }))}
                onChange={onLocaleChange}
              />
              <Switch
                aria-label={t('shell.theme.dark')}
                checked={mode === 'dark'}
                checkedChildren={t('shell.theme.darkShort')}
                unCheckedChildren={t('shell.theme.lightShort')}
                onChange={toggleMode}
              />
            </Space>
          </Layout.Header>
          <Layout.Content style={{ padding: spacing.lg }}>{children}</Layout.Content>
        </Layout>
      </ConfigProvider>
    </div>
  )
}
