import { Flex, Input, Typography } from 'antd'
import { AxisAlert } from '../platform/AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { fetchAboutVersion } from '../platform/about'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import {
  axisLicenceUrl,
  fetchLicences,
  licenceUrl,
  sourceUrl,
  type LicencePackage,
  type Licences,
} from '../platform/licences'
import { PageLayout } from '../platform/PageLayout'
import { SectionPart, SectionParts } from '../platform/SectionParts'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'

const noParameters: string[] = []

/**
 * The open source licences page, open without signing in. It names Axis's licence and links its text and Axis's source, which
 * points at the commit when the server's version names one. Below, the npm and NuGet packages Axis ships sit in two lists, one
 * above the other, that one search box filters by name or licence. Each list has a Filter of its own, kept in the address as
 * `npm.` and `nuget.`, see docs/reference/tables.md#tables. Only a signed-in person sees versions.
 */
export function LicencesPage() {
  const t = useText()
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
    <PageLayout title={t('shell.licences.title')} band>
      <Typography.Paragraph>{t('shell.licences.axis')}</Typography.Paragraph>
      <Flex gap="middle" wrap>
        <a href={axisLicenceUrl} target="_blank" rel="noreferrer">
          {t('shell.licences.licenceText')}
        </a>
        <a href={sourceUrl(version)} target="_blank" rel="noreferrer">
          {t('shell.licences.source')}
        </a>
      </Flex>
      {loadFailed && <AxisAlert data-testid="licences-error" kind="error" message={t('shell.licences.loadFailed')} />}
      {licences && (
        <>
          <Input
            allowClear
            aria-label={t('shell.licences.search')}
            placeholder={t('shell.licences.search')}
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          <SectionParts>
            <PackageList
              title={t('shell.licences.npm')}
              entries={licences.npm.filter(matches)}
              withVersions={hasVersions(licences.npm)}
              prefix="npm."
              testName="licences-npm"
            />
            <PackageList
              title={t('shell.licences.nuget')}
              entries={licences.nuget.filter(matches)}
              withVersions={hasVersions(licences.nuget)}
              prefix="nuget."
              testName="licences-nuget"
            />
          </SectionParts>
        </>
      )}
    </PageLayout>
  )
}

/** Whether the packages carry versions. It looks at the whole list, so a search with no match keeps the column. */
function hasVersions(packages: LicencePackage[]): boolean {
  return packages.some((entry) => entry.version !== undefined)
}

function PackageList({
  title,
  entries,
  withVersions,
  prefix,
  testName,
}: {
  title: string
  entries: LicencePackage[]
  withVersions: boolean
  prefix: string
  testName: string
}) {
  const t = useText()
  const fields = useMemo(
    () => [
      { name: 'name', type: 'text' as const },
      ...(withVersions ? [{ name: 'version', type: 'text' as const }] : []),
      { name: 'licence', type: 'text' as const },
    ],
    [withVersions],
  )
  const { view, setView } = useTableView(fields, recordPageSize, noParameters, false, true, false, prefix)
  const columns: AxisColumn<LicencePackage>[] = [
    {
      key: 'name',
      kind: 'freeText',
      title: t('shell.licences.package'),
      dataIndex: 'name',
      filter: { kind: 'text', value: (entry) => entry.name },
    },
    ...(withVersions
      ? [
          {
            key: 'version',
            kind: 'fixed' as const,
            title: t('shell.licences.version'),
            dataIndex: 'version',
            filter: { kind: 'text' as const, value: (entry: LicencePackage) => entry.version ?? null },
          },
        ]
      : []),
    {
      key: 'licence',
      kind: 'freeText',
      title: t('shell.licences.licence'),
      render: (_value, entry) => {
        const url = licenceUrl(entry.licence)
        return url ? (
          <a href={url} target="_blank" rel="noreferrer">
            {entry.licence}
          </a>
        ) : (
          entry.licence
        )
      },
      filter: { kind: 'text', value: (entry) => entry.licence },
    },
  ]
  return (
    <SectionPart testName={testName} heading={{ title }}>
      <AxisTable<LicencePackage>
        rowKey={(entry) => `${entry.name}/${entry.version ?? ''}/${entry.licence}`}
        view={{ query: view, onChange: setView, paging: false }}
        dataSource={entries}
        emptyText={t('shell.licences.empty')}
        columns={columns}
      />
    </SectionPart>
  )
}
