import { ProLayout } from '@ant-design/pro-components'
import { ConfigProvider, Segmented, Switch } from 'antd'
import dayjs from 'dayjs'
import { useEffect, type ReactNode } from 'react'
import { Link, useLocation } from 'react-router'
import { antLocaleFor, dayjsLocaleFor } from './antLocale'
import type { NavigationItem } from './site'
import { TestUserPicker } from './TestUserPicker'
import { useText } from './texts'
import { useThemeMode } from './themeMode'

interface ShellProps {
  titleKey: string
  navigation: readonly NavigationItem[]
  locales: readonly string[]
  locale: string
  onLocaleChange: (locale: string) => void
  children: ReactNode
}

/**
 * The layout of every site, the platform site and application sites alike: site title and
 * navigation, test user picker, locale switch and theme toggle. Ant Design components and dayjs
 * follow the locale.
 */
export function Shell({ titleKey, navigation, locales, locale, onLocaleChange, children }: ShellProps) {
  const t = useText()
  const { mode, toggleMode } = useThemeMode()
  const { pathname } = useLocation()

  useEffect(() => {
    dayjs.locale(dayjsLocaleFor(locale))
  }, [locale])

  // This provider sets only the locale. It inherits the theme from the provider in theme.tsx.
  return (
    <div data-theme-mode={mode}>
      <ConfigProvider locale={antLocaleFor(locale)}>
        <ProLayout
          title={t(titleKey)}
          // The document title is the site title, not the title of the matching navigation entry.
          pageTitleRender={false}
          logo={false}
          layout="top"
          navTheme={mode === 'dark' ? 'realDark' : 'light'}
          location={{ pathname }}
          route={{
            routes: navigation.map((item) => ({
              key: item.key,
              path: item.path,
              name: t(item.labelKey),
            })),
          }}
          menuItemRender={(item, dom) => <Link to={item.path ?? '/'}>{dom}</Link>}
          actionsRender={() => [
            <TestUserPicker key="user" />,
            <Segmented
              key="locale"
              aria-label={t('shell.locale.label')}
              value={locale}
              options={locales.map((tag) => ({
                value: tag,
                label: t(`shell.locale.${tag}`),
              }))}
              onChange={onLocaleChange}
            />,
            <Switch
              key="theme"
              aria-label={t('shell.theme.dark')}
              checked={mode === 'dark'}
              checkedChildren={t('shell.theme.darkShort')}
              unCheckedChildren={t('shell.theme.lightShort')}
              onChange={toggleMode}
            />,
          ]}
        >
          {children}
        </ProLayout>
      </ConfigProvider>
    </div>
  )
}
