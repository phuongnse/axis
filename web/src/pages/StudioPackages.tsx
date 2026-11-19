import { Flex } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import {
  fetchStudioPackageFile,
  fetchStudioPackageFiles,
  ServerUnreachableError,
  type StudioPackageFileItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { StudioPageHeading } from './StudioPageHeading'

/** The file that names a package, see docs/reference/studio-packages.md#the-packages-entry. */
const manifestPath = 'axis-package.json'

/**
 * One package folder under `packages/`. `label`, `language` and `version` come from its manifest. A package whose manifest is
 * missing or is not valid JSON has its id as the label, and no language or version. `files` are the paths in the package.
 */
interface PackageRow {
  id: string
  label: string
  language: string | null
  version: string | null
  files: string[]
}

/** The packages of one application, or null when they did not load. `application` says which one they were loaded for. */
type PackagesState = { application: string; rows: PackageRow[] | null }

/** The text of a manifest's field, or null when the manifest has none. */
function manifestText(manifest: Record<string, unknown> | null, name: string): string | null {
  const value = manifest?.[name]
  return typeof value === 'string' && value !== '' ? value : null
}

/** The manifest at the package's root, or null when the package has none or it is not a JSON object. */
async function readManifest(
  application: string,
  pkg: string,
  files: readonly string[],
  signal: AbortSignal,
): Promise<Record<string, unknown> | null> {
  if (!files.includes(manifestPath)) {
    return null
  }
  const file = await fetchStudioPackageFile(application, pkg, manifestPath, signal)
  try {
    const manifest: unknown = file === null ? null : JSON.parse(file.content)
    return typeof manifest === 'object' && manifest !== null && !Array.isArray(manifest)
      ? (manifest as Record<string, unknown>)
      : null
  } catch {
    return null
  }
}

/** One row for each package that `items` holds files of, in ordinal order of the id, with its manifest read. */
async function loadRows(
  application: string,
  items: readonly StudioPackageFileItem[],
  signal: AbortSignal,
): Promise<PackageRow[]> {
  const filesOf = new Map<string, string[]>()
  for (const item of items) {
    filesOf.set(item.package, [...(filesOf.get(item.package) ?? []), item.path])
  }
  return Promise.all(
    [...filesOf.keys()].toSorted().map(async (id) => {
      const files = filesOf.get(id)!
      const manifest = await readManifest(application, id, files, signal)
      return {
        id,
        label: manifestText(manifest, 'label') ?? id,
        language: manifestText(manifest, 'language'),
        version: manifestText(manifest, 'version'),
        files: files.toSorted(),
      }
    }),
  )
}

/**
 * The packages of an application's working folder, loaded when the application changes, after a write (`folderRevision`)
 * and after a reconnect. The state is undefined until they load. One loader serves the list and a package's page.
 */
function useStudioPackages(application: string, folderRevision: number): PackagesState | undefined {
  const { generation, lost } = useStudioConnection()
  const [state, setState] = useState<PackagesState>()

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioPackageFiles(application, controller.signal)
      .then((items) => loadRows(application, items, controller.signal))
      .then(
        (rows) => setState({ application, rows }),
        (error: unknown) => {
          if (controller.signal.aborted) {
            return
          }
          if (error instanceof ServerUnreachableError) {
            lost()
            return
          }
          console.warn(`Loading the packages of '${application}' failed`, error)
          setState({ application, rows: null })
        },
      )
    return () => controller.abort()
  }, [application, generation, folderRevision, lost])

  return state?.application === application ? state : undefined
}

// The tables filter every column. Stable arrays keep useTableView from recomputing on each render.
const packageFields = [
  { name: 'label', type: 'text' },
  { name: 'id', type: 'text' },
  { name: 'language', type: 'text' },
  { name: 'version', type: 'text' },
] as const
const fileFields = [{ name: 'path', type: 'text' }] as const
// The tables have no parameters of their own.
const noParameters: readonly string[] = []

/** The address of a package's page. */
function packageAddress(application: string, id: string): string {
  return `/studio?${new URLSearchParams({ app: application, view: 'packages', package: id })}`
}

/**
 * The Packages view, `&view=packages`: the standard table of the application's extension packages, one row for each
 * folder under `packages/`, with its label, id, language and version. A package that has no file has no row, because
 * the list route lists files. The Label is a link that opens the package's page, `&package=<id>`. A language shows
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
        <Link to={packageAddress(application, row.id)} data-testid={`studio-package-${row.id}`}>
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

/**
 * A package's page, `&view=packages&package=<id>`: the package's label as the heading, or its id when it has none, and
 * the package's files by path in the standard table, with Filter and Sort on Path and no search or pager. An id that
 * names no package shows a warning. The page reads the working folder, see docs/reference/studio-packages.md#a-packages-files.
 */
export function StudioPackagePage({
  application,
  packageId,
  folderRevision,
}: {
  application: string
  packageId: string
  folderRevision: number
}) {
  const t = useText()
  const state = useStudioPackages(application, folderRevision)
  const { view, setView } = useTableView(fileFields, recordPageSize, noParameters, false, true, false, 'files.')
  const row = state?.rows?.find((candidate) => candidate.id === packageId)
  const files = useMemo(() => (row?.files ?? []).map((path) => ({ path })), [row])

  if (state === undefined) {
    return null
  }
  if (state.rows === null) {
    return (
      <AxisAlert data-testid="studio-packages-load-error" kind="error" message={t('shell.studio.packages.failed')} />
    )
  }
  if (row === undefined) {
    return (
      <AxisAlert data-testid="studio-package-missing" kind="warning" message={t('shell.studio.packages.missing')} />
    )
  }

  const columns: AxisColumn<{ path: string }>[] = [
    {
      key: 'path',
      kind: 'freeText',
      title: t('shell.studio.packages.path'),
      sortValue: (file) => file.path,
      filter: { kind: 'text', value: (file) => file.path },
      render: (_value, file) => file.path,
    },
  ]
  return (
    <Flex vertical gap="middle">
      <StudioPageHeading title={row.label} />
      <AxisTable<{ path: string }>
        testId="studio-package-files"
        columns={columns}
        rowKey={(file) => file.path}
        dataSource={files}
        view={{ query: view, onChange: setView, paging: false }}
        emptyText={t('shell.studio.packages.empty')}
      />
    </Flex>
  )
}
