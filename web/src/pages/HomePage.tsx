import { PageContainer, ProCard } from '@ant-design/pro-components'
import { Badge, List } from 'antd'
import { useEffect, useState } from 'react'
import { Link } from 'react-router'
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
  const [sites, setSites] = useState<SiteListItem[]>()
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

  if (failed) {
    return null
  }
  return (
    <ProCard title={t('shell.home.sites.title')}>
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
    </ProCard>
  )
}

export function HomePage() {
  const t = useText()
  const serverStatus = useServerStatus()

  return (
    <PageContainer title={t('shell.home.title')}>
      <ProCard ghost direction="column" gutter={[0, 16]}>
        <ProCard title={t('shell.serverStatus.title')}>
          <Badge
            data-testid="server-status"
            status={statusBadge[serverStatus]}
            text={t(`shell.serverStatus.${serverStatus}`)}
          />
        </ProCard>
        <SiteList />
      </ProCard>
    </PageContainer>
  )
}
