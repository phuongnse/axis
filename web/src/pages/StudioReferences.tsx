import { Alert, Flex, Input } from 'antd'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { TableFilterButton, TableFilterChips } from '../platform/TableFilter'
import { emptyFilter, matchesFilter, searchRows, type FilterField, type FilterGroup } from '../platform/tableControls'
import { useContext, useEffect, useState } from 'react'
import {
  fetchResourceReferences,
  ServerUnreachableError,
  type StudioReference,
  type StudioResourceReferences,
  studioResourceAddress,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { StudioCatalogContext } from './schemaFormSections'
import { type StudioChangeRow } from './StudioChangeList'
import { StudioResourceTitle } from './studioResourceTitle'
import { kindName, kindOrder, nameCollator } from './studioViews'

type Direction = 'uses' | 'usedBy'

/** Opens a resource. `path` is set for a Used by entry, where the reference sits in that resource. */
export type OpenResource = (id: string, path: string | null) => void

/**
 * The Used by tab of a selected resource: what uses it, each entry a name that opens it at the reference. A load
 * failure shows an alert in its place.
 */
export function StudioReferences({ application, resourceId }: { application: string; resourceId: string }) {
  const t = useText()
  const { generation, lost } = useStudioConnection()
  const [references, setReferences] = useState<StudioResourceReferences>()
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    fetchResourceReferences(application, resourceId, controller.signal).then(setReferences, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the references of '${resourceId}' failed`, error)
      setFailed(true)
    })
    return () => controller.abort()
  }, [application, resourceId, generation, lost])

  if (failed) {
    return <Alert type="error" title={t('shell.studio.referencesFailed')} />
  }
  if (!references) {
    return null
  }
  // Proposed: the Used by tab of a resource lists what uses it, the references a delete or a rename breaks. What the
  // resource uses is not listed: its editor shows it, and nothing it uses breaks when it goes. The same resource used the
  // same way shows once.
  return <ReferenceTable application={application} entries={references.usedBy} />
}

/**
 * Proposed: what uses a resource as the standard table, `AxisTable`: the other resource by its label, the link that
 * opens it at the reference, its kind, and how it uses this one, sorted by kind in the sidebar's order, then by name.
 * Search looks in all three, and the filter picks kinds, as every list's do.
 */
function ReferenceTable({ application, entries }: { application: string; entries: StudioReference[] }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<FilterGroup>(emptyFilter)
  const rows = useReferenceRows(entries, application, 'usedBy')
  const collator = nameCollator()
  const sorted = [...rows].sort((a, b) => (a.order ?? 999) - (b.order ?? 999) || collator.compare(a.name, b.name))
  const kinds = [...new Set(sorted.map((row) => row.kind))]
  const filterFields: FilterField[] = [
    {
      key: 'kind',
      label: t('shell.studio.references.kind'),
      type: 'enum',
      options: kinds.map((kind) => ({ value: kind, label: kind })),
    },
  ]
  const columns: AxisColumn<StudioChangeRow>[] = [
    {
      key: 'name',
      kind: 'freeText',
      title: t('shell.studio.references.name'),
      searchText: (row) => row.name,
      render: (_, row) =>
        row.to ? (
          <Link to={row.to} style={{ color: colour.primaryText }}>
            {row.name}
          </Link>
        ) : (
          row.name
        ),
    },
    {
      key: 'kind',
      kind: 'fixed',
      title: t('shell.studio.references.kind'),
      searchText: (row) => row.kind,
      render: (_, row) => row.kind,
    },
    {
      key: 'how',
      kind: 'freeText',
      title: t('shell.studio.references.how'),
      searchText: (row) => row.change,
      render: (_, row) => row.change,
    },
  ]
  const shown = searchRows(sorted, columns, search).filter((row) =>
    matchesFilter(filter, (field) => (field === 'kind' ? row.kind : null)),
  )
  return (
    <AxisTable<StudioChangeRow>
      testId="studio-used-by"
      dataSource={shown}
      rowKey={(row) => row.key}
      columns={columns}
      emptyText={t('shell.studio.usedBy.none')}
      pagination={false}
      toolbar={{
        view: (
          <Flex gap="small" align="center">
            <Input.Search
              allowClear
              aria-label={t('shell.studio.search')}
              placeholder={t('shell.studio.search')}
              style={{ maxWidth: 320 }}
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
            <TableFilterButton fields={filterFields} filter={filter} onApply={setFilter} />
          </Flex>
        ),
      }}
      below={<TableFilterChips fields={filterFields} filter={filter} onChange={setFilter} />}
    />
  )
}

/** The rows of one direction: each other resource once per way it is used, by its label, kind and relation in words. */
function useReferenceRows(entries: StudioReference[], application: string, direction: Direction): StudioChangeRow[] {
  const t = useText()
  const titleOf = useContext(StudioResourceTitle)
  const catalog = useContext(StudioCatalogContext)
  const seen = new Set<string>()
  const rows: StudioChangeRow[] = []
  for (const entry of entries) {
    const node = entry.node
    const id = node.type === 'text' ? node.key : node.id
    const key = `${node.type}/${id}/${entry.relation}`
    if (seen.has(key)) {
      continue
    }
    seen.add(key)
    const relationKey = `shell.studio.relation.${entry.relation.replace(/ ([a-z])/g, (_, letter: string) => letter.toUpperCase())}`
    const relation = t(relationKey) === relationKey ? '' : t(relationKey)
    if (node.type === 'resource') {
      rows.push({
        key,
        name: titleOf(node.id) ?? node.name ?? node.file,
        kind: kindName(node.kind, t),
        change: relation,
        to: studioResourceAddress(application, node.id, direction === 'usedBy' ? entry.path : null),
        order: kindOrder(node.kind),
      })
    } else if (node.type === 'module') {
      rows.push({
        key,
        name: catalog?.modules.find((module) => module.id === node.id)?.label ?? node.id,
        kind: t('shell.studio.release.part.module'),
        change: relation,
      })
    } else if (node.type === 'package') {
      rows.push({
        key,
        name: catalog?.packages.find((pkg) => pkg.id === node.id)?.label ?? node.id,
        kind: t('shell.studio.release.part.package'),
        change: relation,
      })
    } else {
      rows.push({ key, name: node.key, kind: t('shell.studio.references.textKeys'), change: relation })
    }
  }
  return rows
}
