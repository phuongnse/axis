import { Alert, Badge, Card, List } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { PageLayout } from '../platform/PageLayout'
import { useServerStatus, type ServerStatus } from '../platform/serverStatus'
import { fetchSites, type SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { usePlatform } from './context'

const statusBadge: Record<ServerStatus, 'processing' | 'success' | 'error'> = {
  checking: 'processing',
  ready: 'success',
  unavailable: 'error',
}

/** The site title in the current locale, or in the site's first locale when it lacks the current one. */
function siteTitle(site: SiteListItem, locale: string): string {
  return site.titles[locale] ?? Object.values(site.titles)[0] ?? site.path
}

function SiteList() {
  const t = useText()
  const { locale } = usePlatform().current
  // `null` when nobody is signed in.
  const [sites, setSites] = useState<SiteListItem[] | null>()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchSites(controller.signal)
      .then(setSites)
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the sites failed', error)
          setFailed(true)
        }
      })
    return () => controller.abort()
  }, [])

  return (
    <Card title={t('shell.home.sites.title')}>
      {failed ? (
        <Alert data-testid="sites-error" type="error" message={t('shell.home.sites.loadFailed')} />
      ) : sites === null ? (
        <Alert data-testid="sites-sign-in" type="info" message={t('shell.home.sites.signIn')} />
      ) : (
        <List
          loading={!sites}
          dataSource={sites ?? []}
          locale={{ emptyText: t('shell.home.sites.empty') }}
          renderItem={(site) => (
            <List.Item key={site.path}>
              <Link to={`/${site.path}`}>{siteTitle(site, locale)}</Link>
            </List.Item>
          )}
        />
      )}
    </Card>
  )
}

export function HomePage() {
  const t = useText()
  const serverStatus = useServerStatus()

  return (
    <PageLayout title={t('shell.home.title')}>
      <Card title={t('shell.serverStatus.title')}>
        <Badge
          data-testid="server-status"
          status={statusBadge[serverStatus]}
          text={t(`shell.serverStatus.${serverStatus}`)}
        />
      </Card>
      <SiteList />
    </PageLayout>
  )
}
