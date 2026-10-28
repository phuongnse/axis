import { Alert, Badge, Card, Empty, Flex, Spin } from 'antd'
import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { PageLayout } from '../platform/PageLayout'
import { statusBadge, useServerStatus } from '../platform/serverStatus'
import { fetchSites, type SiteListItem } from '../platform/site'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { usePlatform } from './context'

/** The site title in the current locale, or in the site's first locale when it lacks the current one. */
function siteTitle(site: SiteListItem, locale: string): string {
  return site.titles[locale] ?? Object.values(site.titles)[0] ?? site.path
}

function SiteList() {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
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
        <Alert data-testid="sites-error" type="error" title={t('shell.home.sites.loadFailed')} />
      ) : sites === null ? (
        <Alert data-testid="sites-sign-in" type="info" title={t('shell.home.sites.signIn')} />
      ) : sites === undefined ? (
        <Spin />
      ) : sites.length === 0 ? (
        <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('shell.home.sites.empty')} />
      ) : (
        <Flex vertical>
          {sites.map((site) => (
            <Flex key={site.path} style={{ paddingBlock: spacing.sm, borderBlockEnd: `1px solid ${colour.divider}` }}>
              <Link to={`/${site.path}`}>{siteTitle(site, locale)}</Link>
            </Flex>
          ))}
        </Flex>
      )}
    </Card>
  )
}

export function HomePage() {
  const t = useText()
  const serverStatus = useServerStatus()
  const [searchParams] = useSearchParams()
  const signInFailed = searchParams.get('signIn') === 'failed'

  return (
    <PageLayout title={t('shell.home.title')}>
      {signInFailed && (
        <Alert data-testid="sign-in-failed" role="alert" type="warning" showIcon title={t('shell.home.signInFailed')} />
      )}
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
