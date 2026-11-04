import { Alert, Col, Input, Row, Table, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { fetchAboutVersion } from '../platform/about'
import {
  axisLicenceUrl,
  fetchLicences,
  licenceUrl,
  sourceUrl,
  type LicencePackage,
  type Licences,
} from '../platform/licences'
import { PageLayout } from '../platform/PageLayout'
import { useText } from '../platform/texts'
import { usePlatform } from './context'

/**
 * The open source licences page, open without signing in. It names Axis's licence and links its text and Axis's source, which
 * points at the commit when the server's version names one. Below, the npm and NuGet packages Axis ships sit in two lists, side
 * by side and stacked at narrow width, that one search box filters by name or licence. Only a signed-in person sees versions.
 */
export function LicencesPage() {
  const t = useText()
  const platform = usePlatform()
  const [licences, setLicences] = useState<Licences>()
  const [version, setVersion] = useState<string | null>(null)
  const [loadFailed, setLoadFailed] = useState(false)
  const [search, setSearch] = useState('')

  useEffect(() => {
    const controller = new AbortController()
    const aboutVersion = fetchAboutVersion(controller.signal).catch((error: unknown) => {
      console.warn('Loading the server version failed', error)
      return null
    })
    Promise.all([fetchLicences(controller.signal), aboutVersion]).then(
      ([loaded, loadedVersion]) => {
        setLicences(loaded)
        setVersion(loadedVersion)
      },
      (error: unknown) => {
        if (!controller.signal.aborted) {
          console.warn('Loading the licences failed', error)
          setLoadFailed(true)
        }
      },
    )
    return () => controller.abort()
  }, [])

  const query = search.trim().toLowerCase()
  const matches = (entry: LicencePackage) =>
    query === '' || entry.name.toLowerCase().includes(query) || entry.licence.toLowerCase().includes(query)

  return (
    <PageLayout
      title={t('shell.licences.title')}
      breadcrumb={[{ label: t(platform.site.titleKey), to: '/' }, { label: t('shell.licences.title') }]}
    >
      <Typography.Paragraph>
        {t('shell.licences.axis')}{' '}
        <a href={axisLicenceUrl} target="_blank" rel="noreferrer">
          {t('shell.licences.licenceText')}
        </a>
        {' · '}
        <a href={sourceUrl(version)} target="_blank" rel="noreferrer">
          {t('shell.licences.source')}
        </a>
      </Typography.Paragraph>
      {loadFailed && <Alert data-testid="licences-error" type="error" title={t('shell.licences.loadFailed')} />}
      {licences && (
        <>
          <Input
            allowClear
            aria-label={t('shell.licences.search')}
            placeholder={t('shell.licences.search')}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          <Row gutter={[16, 16]}>
            <Col xs={24} md={12}>
              <PackageList
                title={t('shell.licences.npm')}
                entries={licences.npm.filter(matches)}
                testId="licences-npm"
              />
            </Col>
            <Col xs={24} md={12}>
              <PackageList
                title={t('shell.licences.nuget')}
                entries={licences.nuget.filter(matches)}
                testId="licences-nuget"
              />
            </Col>
          </Row>
        </>
      )}
    </PageLayout>
  )
}

function PackageList({ title, entries, testId }: { title: string; entries: LicencePackage[]; testId: string }) {
  const t = useText()
  const withVersions = entries.some((entry) => entry.version !== undefined)
  return (
    <div data-testid={testId}>
      <Typography.Title level={5}>{title}</Typography.Title>
      <Table<LicencePackage>
        size="small"
        rowKey={(entry) => `${entry.name}/${entry.version ?? ''}/${entry.licence}`}
        pagination={false}
        dataSource={entries}
        columns={[
          { title: t('shell.licences.package'), dataIndex: 'name' },
          ...(withVersions ? [{ title: t('shell.licences.version'), dataIndex: 'version' }] : []),
          {
            title: t('shell.licences.licence'),
            dataIndex: 'licence',
            render: (licence: string) => {
              const url = licenceUrl(licence)
              return url ? (
                <a href={url} target="_blank" rel="noreferrer">
                  {licence}
                </a>
              ) : (
                licence
              )
            },
          },
        ]}
      />
    </div>
  )
}
