import { ProLayout } from '@ant-design/pro-components'
import { Segmented, Switch } from 'antd'
import type { ReactNode } from 'react'
import type { SiteMetadata } from './site'
import { useText } from './texts'
import { useThemeMode } from './themeMode'

interface ShellProps {
  site: SiteMetadata
  locale: string
  onLocaleChange: (locale: string) => void
  children: ReactNode
}

/** The platform layout: site title and navigation, locale switch and theme toggle. */
export function Shell({ site, locale, onLocaleChange, children }: ShellProps) {
  const t = useText()
  const { mode, toggleMode } = useThemeMode()

  return (
    <div data-theme-mode={mode}>
      <ProLayout
        title={t(site.titleKey)}
        // The document title is the site title, not the title of the matching navigation entry.
        pageTitleRender={false}
        logo={false}
        layout="top"
        navTheme={mode === 'dark' ? 'realDark' : 'light'}
        location={{ pathname: window.location.pathname }}
        route={{
          routes: site.navigation.map((item) => ({
            key: item.key,
            path: item.path,
            name: t(item.labelKey),
          })),
        }}
        // No client-side router yet: each entry is a plain link.
        menuItemRender={(item, dom) => <a href={item.path}>{dom}</a>}
        actionsRender={() => [
          <Segmented
            key="locale"
            aria-label={t('shell.locale.label')}
            value={locale}
            options={site.locales.available.map((tag) => ({
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
    </div>
  )
}
