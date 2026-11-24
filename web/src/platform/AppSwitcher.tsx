import { AppstoreOutlined } from '@ant-design/icons'
import { Dropdown } from 'antd'
import { useEffect, useState } from 'react'
import { useLocation } from 'react-router'
import { AxisButton } from './AxisButton'
import { fetchCurrentUser, type CurrentUser } from './currentUser'
import { menuOf, type AxisAction } from './menuActions'
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
 * administrator and Billing, a link to the operator's billing page, for an administrator in a cell with a billing page set. The site or tool of the current page is drawn selected and marked as the current page.
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

  const sitesEntries: AxisAction[] = sites.map((site) => {
    const address = siteAddress(site.path)
    return {
      key: `site:${site.path}`,
      label: siteTitle(site, locale),
      href: address,
      groupKey: 'shell.appSwitcher.sites',
      selected: isCurrent(address, pathname),
    }
  })
  const tools: AxisAction[] = []
  const tool = (key: string, labelKey: string, address: string) => {
    tools.push({
      key,
      labelKey,
      href: address,
      groupKey: 'shell.appSwitcher.tools',
      selected: isCurrent(address, pathname),
    })
  }
  if (user.studio) {
    tool('studio', 'shell.appSwitcher.studio', '/studio')
  }
  if (user.console) {
    tool('console', 'shell.appSwitcher.console', '/console')
  }
  if (user.users) {
    tool('users', 'shell.appSwitcher.users', '/users')
  }
  if (user.billingUrl) {
    // An outside link, so it is never the current page.
    tools.push({
      key: 'billing',
      labelKey: 'shell.appSwitcher.billing',
      href: user.billingUrl,
      groupKey: 'shell.appSwitcher.tools',
      selected: false,
    })
  }
  const menu = menuOf([...sitesEntries, ...tools], t)
  const label = t('shell.appSwitcher.label')

  return (
    <Dropdown trigger={['click']} menu={{ ...menu, 'aria-label': label }}>
      <AxisButton role="subtle" nameKey="shell.appSwitcher.label" icon={<AppstoreOutlined />} />
    </Dropdown>
  )
}
