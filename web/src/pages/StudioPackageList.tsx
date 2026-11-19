import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisTable } from '../platform/AxisTable'
import {
  fetchStudioPackageFile,
  packageManifestFile as manifestFile,
  type StudioPackageFileItem,
} from '../platform/studio'
import { studioPackageAddress } from '../platform/studioAddress'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'

interface PackageRow {
  id: string
  label: string
  language: string | null
  version: string | null
}

const noFields: [] = []

/**
 * The Packages entry's list, drawn only so the `studio` preview reaches a package: #3989 builds it from the agreed
 * patterns, see docs/reference/studio-packages.md#the-packages-entry.
 */
export function StudioPackageList({
  application,
  files,
}: {
  application: string
  files: readonly StudioPackageFileItem[]
}) {
  const t = useText()
  const { view, setView } = useTableView(noFields, recordPageSize)
  const ids = useMemo(() => [...new Set(files.map((item) => item.package))].sort(), [files])
  const [manifests, setManifests] = useState<Record<string, { label?: string; language?: string; version?: string }>>(
    {},
  )

  useEffect(() => {
    const controller = new AbortController()
    for (const id of ids) {
      fetchStudioPackageFile(application, id, manifestFile, controller.signal).then(
        (loaded) => setManifests((current) => ({ ...current, [id]: JSON.parse(loaded.content) as object })),
        () => {},
      )
    }
    return () => controller.abort()
  }, [application, ids])

  const rows: PackageRow[] = ids.map((id) => ({
    id,
    label: manifests[id]?.label ?? id,
    language: manifests[id]?.language ?? (manifests[id] ? 'csharp' : null),
    version: manifests[id]?.version ?? null,
  }))

  return (
    <AxisTable<PackageRow>
      rowKey={(row) => row.id}
      view={{ query: view, onChange: setView, paging: false }}
      dataSource={rows}
      emptyText={t('shell.studio.packages.package')}
      columns={[
        {
          key: 'label',
          kind: 'freeText',
          title: t('shell.studio.packages.package'),
          render: (_value, row) => <Link to={studioPackageAddress(application, row.id)}>{row.label}</Link>,
        },
        { key: 'id', kind: 'fixed', title: t('shell.studio.id'), render: (_value, row) => row.id },
        {
          key: 'language',
          kind: 'fixed',
          title: t('shell.studio.packages.language'),
          render: (_value, row) => (row.language ? t(`shell.studio.package.language.${row.language}`) : null),
        },
        {
          key: 'version',
          kind: 'fixed',
          title: t('shell.studio.packages.version'),
          render: (_value, row) => row.version,
        },
      ]}
    />
  )
}
