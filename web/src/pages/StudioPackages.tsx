import { Link } from 'react-router'
import { studioPackageAddress } from '../platform/studioAddress'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { useStudioPackages, type PackageRow } from './useStudioPackages'

// The table filters every column. Stable arrays keep useTableView from recomputing on each render.
const packageFields = [
  { name: 'label', type: 'text' },
  { name: 'id', type: 'text' },
  { name: 'language', type: 'text' },
  { name: 'version', type: 'text' },
] as const
// The table has no parameters of its own.
const noParameters: readonly string[] = []

/**
 * The Packages view, `&view=packages`: the standard table of the application's extension packages, one row for each
 * folder under `packages/`, with its label, id, language and version. A package that has no file has no row, because
 * the list route lists files. The Label is a link that opens the package's workspace, `&package=<id>`. A language shows
 * as its name, such as TypeScript, and an unknown one as it is written. The table holds all its rows, has Filter on
 * every column and Sort on Label and ID, and no search or pager, see docs/reference/studio-packages.md#the-packages-entry.
 */
export function StudioPackages({ application, folderRevision }: { application: string; folderRevision: number }) {
  const t = useText()
  const state = useStudioPackages(application, folderRevision)
  const { view, setView } = useTableView(packageFields, recordPageSize, noParameters, false, true, false, 'packages.')
  const rows = state?.rows ?? []
  const languageText = (language: string | null) => {
    if (language === null) {
      return null
    }
    const key = `shell.studio.packages.language.${language}`
    const text = t(key)
    return text === key ? language : text
  }

  const columns: AxisColumn<PackageRow>[] = [
    {
      key: 'label',
      kind: 'freeText',
      title: t('shell.studio.packages.label'),
      sortValue: (row) => row.label,
      filter: { kind: 'text', value: (row) => row.label },
      render: (_value, row) => (
        <Link to={studioPackageAddress(application, row.id)} data-testid={`studio-package-${row.id}`}>
          {row.label}
        </Link>
      ),
    },
    {
      key: 'id',
      kind: 'freeText',
      title: t('shell.studio.id'),
      sortValue: (row) => row.id,
      filter: { kind: 'text', value: (row) => row.id },
      render: (_value, row) => row.id,
    },
    {
      key: 'language',
      kind: 'fixed',
      title: t('shell.studio.packages.language'),
      filter: { kind: 'text', value: (row) => languageText(row.language) },
      render: (_value, row) => languageText(row.language),
    },
    {
      key: 'version',
      kind: 'fixed',
      title: t('shell.studio.packages.version'),
      filter: { kind: 'text', value: (row) => row.version },
      render: (_value, row) => row.version,
    },
  ]

  return (
    <AxisTable<PackageRow>
      testId="studio-packages"
      columns={columns}
      rowKey={(row) => row.id}
      dataSource={rows}
      view={{ query: view, onChange: setView, paging: false }}
      loading={state === undefined}
      emptyText={t('shell.studio.packages.empty')}
      failure={
        state?.rows === null ? (
          <AxisAlert
            data-testid="studio-packages-load-error"
            kind="error"
            message={t('shell.studio.packages.failed')}
          />
        ) : undefined
      }
    />
  )
}
