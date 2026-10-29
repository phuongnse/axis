import { AppstoreOutlined } from '@ant-design/icons'
import { Button, Dropdown, type MenuProps } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { fetchCurrentUser, type CurrentUser } from './currentUser'
import { fetchSites, siteTitle, type SiteListItem } from './site'
import { useText } from './texts'

/**
 * The "Switch to" button in the shell header. Its menu lists the person's sites, then Studio for a
 * user who may use it, the operator console for a user who may use that and Users for an
 * administrator. It renders nothing
 * while the user loads, when nobody is signed in, or when loading the user fails. When loading the
 * sites fails, the menu lists only the tools.
 */
export function AppSwitcher({ locale }: { locale: string }) {
  const t = useText()
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

  const tools: NonNullable<MenuProps['items']> = []
  if (user.studio) {
    tools.push({ key: 'studio', label: <Link to="/studio">{t('shell.appSwitcher.studio')}</Link> })
  }
  if (user.console) {
    tools.push({ key: 'console', label: <Link to="/console">{t('shell.appSwitcher.console')}</Link> })
  }
  if (user.users) {
    tools.push({ key: 'users', label: <Link to="/users">{t('shell.appSwitcher.users')}</Link> })
  }
  const items: NonNullable<MenuProps['items']> = [
    ...sites.map((site) => ({
      key: `site:${site.path}`,
      label: <Link to={`/${site.path}`}>{siteTitle(site, locale)}</Link>,
    })),
    ...(sites.length > 0 && tools.length > 0 ? [{ type: 'divider' as const }] : []),
    ...tools,
  ]
  const label = t('shell.appSwitcher.label')

  return (
    <Dropdown trigger={['click']} menu={{ items, 'aria-label': label }}>
      <Button type="text" aria-label={label} icon={<AppstoreOutlined aria-hidden />} />
    </Dropdown>
  )
}
