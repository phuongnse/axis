import { AppstoreOutlined } from '@ant-design/icons'
import { Dropdown, type MenuProps } from 'antd'
import { Link } from 'react-router'
import { AxisButton } from './AxisButton'
import { localizedTitle, useCurrentUser, useSites } from './launcher'
import { useText } from './texts'

/**
 * Proposed: the app switcher in the header, in place of the Studio link. It lists the user's sites, then the tools
 * of their roles, so they can move from any site to another, to Studio or to the console. With nobody signed in it
 * draws nothing.
 */
export function AppSwitcher({ locale }: { locale: string }) {
  const t = useText()
  const user = useCurrentUser()
  const { sites } = useSites()
  if (!user) {
    return null
  }
  const tools = [
    ...(user.studio ? [{ key: 'tool:studio', label: <Link to="/studio">{t('shell.studio.link')}</Link> }] : []),
    ...(user.console ? [{ key: 'tool:console', label: <Link to="/console">{t('shell.console.link')}</Link> }] : []),
  ]
  const items: MenuProps['items'] = [
    {
      type: 'group',
      label: t('shell.switcher.sites'),
      children: (sites ?? []).map((site) => ({
        key: `site:${site.path}`,
        label: <Link to={`/${site.path}`}>{localizedTitle(site.titles, locale, site.path)}</Link>,
      })),
    },
    ...(tools.length > 0
      ? [{ type: 'divider' as const }, { type: 'group' as const, label: t('shell.switcher.tools'), children: tools }]
      : []),
  ]
  return (
    <Dropdown trigger={['click']} menu={{ items, 'aria-label': t('shell.switcher.label') }}>
      <span style={{ display: 'inline-flex' }}>
        <AxisButton role="icon" nameKey="shell.switcher.label" icon={<AppstoreOutlined />} aria-haspopup="menu" />
      </span>
    </Dropdown>
  )
}
