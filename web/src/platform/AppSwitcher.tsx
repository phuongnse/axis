import { AppstoreOutlined } from '@ant-design/icons'
import { Dropdown, type MenuProps } from 'antd'
import { useEffect, useState } from 'react'
import { Link, useLocation } from 'react-router'
import { AxisButton } from './AxisButton'
import { fetchCurrentUser, type CurrentUser } from './currentUser'
import { fetchSites, siteTitle, type SiteListItem } from './site'
import { useText } from './texts'
import { siteAddress } from './siteLinks'

/** Whether the page address is the address of a site or a tool, or lies inside it. */
function isCurrent(address: string, pathname: string): boolean {
  return pathname === address || address === '/' || pathname.startsWith(`${address}/`)
}

/**
 * The "Switch to" button in the shell header. Its menu lists the person's sites, then Studio for a
 * user who may use it, the operator console for a user who may use that, Users for an
 * administrator and Billing, a link to the billing portal, for an administrator in a cell. The site or tool of the current page is drawn selected and marked as the current page.
 * It renders nothing
 * while the user loads, when nobody is signed in, or when loading the user fails. When loading the
 * sites fails, the menu lists only the tools.
 */
export function AppSwitcher({ locale }: { locale: string }) {
  const t = useText()
  const { pathname } = useLocation()
  const [user, setUser] = useState<CurrentUser | null | undefined>(undefined)
  const [sites, setSites] = useState<SiteListItem[]>([])

  useEffect(() => {
    const controller = new AbortController()
    const warn = (message: string) => (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn(message, error)
      }
    }
    fetchCurrentUser(controller.signal)
      .then((current) => {
        setUser(current)
        if (current === null) {
          return undefined
        }
        return fetchSites(controller.signal).then((listed) => setSites(listed ?? []), warn('Loading the sites failed'))
      })
      .catch(warn('Loading the current user failed'))
    return () => controller.abort()
  }, [])

  if (!user) {
    return null
  }

  const selectedKeys: string[] = []
  const entry = (key: string, address: string, text: string) => {
    const current = isCurrent(address, pathname)
    if (current) {
      selectedKeys.push(key)
    }
    return {
      key,
      label: (
        <Link to={address} aria-current={current ? 'page' : undefined}>
          {text}
        </Link>
      ),
    }
  }
  const tools: NonNullable<MenuProps['items']> = []
  if (user.studio) {
    tools.push(entry('studio', '/studio', t('shell.appSwitcher.studio')))
  }
  if (user.console) {
    tools.push(entry('console', '/console', t('shell.appSwitcher.console')))
  }
  if (user.users) {
    tools.push(entry('users', '/users', t('shell.appSwitcher.users')))
  }
  if (user.billingUrl) {
    // An outside link, so it is never the current page.
    tools.push({ key: 'billing', label: <a href={user.billingUrl}>{t('shell.appSwitcher.billing')}</a> })
  }
  const items: NonNullable<MenuProps['items']> = []
  if (sites.length > 0) {
    items.push({
      type: 'group',
      key: 'sites',
      label: t('shell.appSwitcher.sites'),
      children: sites.map((site) => entry(`site:${site.path}`, siteAddress(site.path), siteTitle(site, locale))),
    })
  }
  if (tools.length > 0) {
    items.push({ type: 'group', key: 'tools', label: t('shell.appSwitcher.tools'), children: tools })
  }
  const label = t('shell.appSwitcher.label')

  return (
    <Dropdown trigger={['click']} menu={{ items, selectable: true, selectedKeys, 'aria-label': label }}>
      <AxisButton role="icon" nameKey="shell.appSwitcher.label" icon={<AppstoreOutlined />} />
    </Dropdown>
  )
}
