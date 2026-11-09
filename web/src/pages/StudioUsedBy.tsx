import { Alert, Select } from 'antd'
import { useContext, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { sortByName } from '../platform/listOrder'
import {
  fetchResourceReferences,
  relationTextKey,
  resourceTitle,
  ServerUnreachableError,
  type StudioReference,
  type StudioResourceItem,
  type StudioResourceReferences,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { studioResourceAddress } from '../platform/studioAddress'
import { kindSingular, sidebarRank } from '../platform/studioKinds'
import { recordPageSize } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { PlatformContext } from './context'

/** One resource that uses the open one, in one way. `path` is where the reference sits in that resource. */
interface UsedByRow {
  key: string
  id: string
  kind: string
  label: string
  how: string
  path: string
}

/** The columns the view's sort can name, see docs/reference/studio-resource-form.md#used-by. */
const usedByFields = [
  { name: 'label', type: 'text' },
  { name: 'kind', type: 'text' },
] as const

/**
 * The resources that use the open one, from the `usedBy` entries of its references. A resource that uses it the same
 * way shows once, at the path of its first reference, and the rows sort by kind in the sidebar's order, then by label.
 */
function usedByRows(
  entries: readonly StudioReference[],
  resources: readonly StudioResourceItem[],
  locale: string,
  t: (key: string) => string,
): UsedByRow[] {
  const rows = new Map<string, UsedByRow>()
  for (const { relation, path, node } of entries) {
    if (node.type !== 'resource') {
      continue
    }
    const key = `${node.id}|${relation}`
    if (rows.has(key)) {
      continue
    }
    const item = resources.find((resource) => resource.id === node.id)
    rows.set(key, {
      key,
      id: node.id,
      kind: node.kind,
      label: item ? resourceTitle(item, locale, t) : (node.name ?? kindSingular(node.kind, t)),
      how: t(relationTextKey(relation)),
      path,
    })
  }
  return sortByName(
    [...rows.values()],
    (row) => row.label,
    locale,
    (row) => sidebarRank(row.kind),
  )
}

/**
 * The Used by tab of a resource: the resources that use it, each by its label as a link that opens it at the path of
 * the reference, with search and a kind filter. It loads when the tab mounts.
 */
export function StudioUsedBy({
  application,
  resourceId,
  resources,
}: {
  application: string
  resourceId: string
  resources: StudioResourceItem[]
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { generation, lost } = useStudioConnection()
  const [usedBy, setUsedBy] = useState<StudioReference[]>()
  const [failed, setFailed] = useState(false)
  const [kind, setKind] = useState<string>()

  useEffect(() => {
    const controller = new AbortController()
    fetchResourceReferences(application, resourceId, controller.signal).then(
      (references: StudioResourceReferences) => {
        setUsedBy(references.usedBy)
        setFailed(false)
      },
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn(`Loading the references of '${resourceId}' failed`, error)
        setFailed(true)
      },
    )
    return () => controller.abort()
  }, [application, resourceId, generation, lost])

  const rows = useMemo(() => (usedBy ? usedByRows(usedBy, resources, locale, t) : []), [usedBy, resources, locale, t])
  const kinds = useMemo(
    () => [...new Set(rows.map((row) => row.kind))].sort((left, right) => sidebarRank(left) - sidebarRank(right)),
    [rows],
  )
  const shown = useMemo(() => rows.filter((row) => kind === undefined || row.kind === kind), [rows, kind])
  const { view, setView } = useTableView(usedByFields, recordPageSize, [], true)

  const columns: AxisColumn<UsedByRow>[] = [
    {
      key: 'label',
      kind: 'freeText',
      title: t('shell.studio.usedBy.name'),
      searchText: (row) => row.label,
      sortValue: (row) => row.label,
      render: (_value, row) => <Link to={studioResourceAddress(application, row.id, row.path)}>{row.label}</Link>,
    },
    {
      key: 'kind',
      kind: 'fixed',
      title: t('shell.studio.kind'),
      sortValue: (row) => sidebarRank(row.kind),
      render: (_value, row) => kindSingular(row.kind, t),
    },
    {
      key: 'how',
      kind: 'freeText',
      title: t('shell.studio.usedBy.how'),
      render: (_value, row) => row.how,
    },
  ]

  return (
    <AxisTable<UsedByRow>
      testId="studio-used-by"
      columns={columns}
      rowKey={(row) => row.key}
      dataSource={shown}
      view={{ query: view, onChange: setView }}
      loading={usedBy === undefined && !failed}
      emptyText={t('shell.studio.usedBy.none')}
      // oxlint-disable-next-line axis/no-antd-alert -- Predates AxisAlert.
      failure={failed ? <Alert type="error" title={t('shell.studio.referencesFailed')} /> : undefined}
      toolbar={{
        view: (
          <Select
            allowClear
            virtual={false}
            aria-label={t('shell.studio.kind')}
            placeholder={t('shell.studio.kind')}
            style={{ minWidth: 160 }}
            value={kind}
            onChange={(value: string | undefined) => setKind(value)}
            options={kinds.map((item) => ({ value: item, label: kindSingular(item, t) }))}
          />
        ),
      }}
    />
  )
}
