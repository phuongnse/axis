import { Alert, Flex, Input, Segmented } from 'antd'
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
import { EmptyState } from '../platform/EmptyState'
import { useDesignTokens } from '../platform/themeMode'
import { neverUsed, StudioCatalogContext, usesShownByForm } from './schemaFormSections'
import { type StudioChangeRow } from './StudioChangeList'
import { StudioResourceTitle } from './studioResourceTitle'
import { kindName, kindOrder, nameCollator } from './studioViews'

type Direction = 'uses' | 'usedBy'

/** Opens a resource. `path` is set for a Used by entry, where the reference sits in that resource. */
export type OpenResource = (id: string, path: string | null) => void

/**
 * The Dependencies of a selected resource: what uses it and what it uses, each entry a name that opens it. A load
 * failure shows an alert in their place.
 */
export function StudioReferences({
  application,
  resourceId,
  kind,
}: {
  application: string
  resourceId: string
  kind: string
}) {
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
  // Proposed: the Dependencies tab of a resource, as low-code tools show a component's dependencies: one table at a time,
  // Used by or Uses, chosen above it. Uses leaves out what the kind's form already shows, its labels' texts on every kind,
  // and Used by is left out for a kind nothing can refer to, as it never applies. The same resource used the same way
  // shows once.
  const shownByForm = usesShownByForm[kind] ?? new Set(['text'])
  const uses = references.uses.filter(
    (entry) => !shownByForm.has(entry.node.type === 'resource' ? entry.node.kind : entry.node.type),
  )
  const directions: Direction[] = [
    ...(neverUsed.has(kind) ? [] : (['usedBy'] as const)),
    ...(usesShownByForm[kind] && uses.length === 0 ? [] : (['uses'] as const)),
  ]
  if (directions.length === 0) {
    return <EmptyState text={t('shell.studio.usedBy.none')} />
  }
  return (
    <ReferenceTable application={application} directions={directions} entries={{ uses, usedBy: references.usedBy }} />
  )
}

/**
 * Proposed: the entries of one direction as the standard table, `AxisTable`: the other resource by its label, the link
 * that opens it, its kind, and how the one uses the other, sorted by kind in the sidebar's order, then by name. Search
 * looks in all three, and the filter picks kinds, as every list's do. A choice above the table switches between Used by
 * and Uses, when both apply.
 */
function ReferenceTable({
  application,
  directions,
  entries,
}: {
  application: string
  directions: Direction[]
  entries: Record<Direction, StudioReference[]>
}) {
  const t = useText()
  const { colour } = useDesignTokens()
  const [direction, setDirection] = useState<Direction>(directions[0])
  const [search, setSearch] = useState('')
  const [filter, setFilter] = useState<FilterGroup>(emptyFilter)
  const rows = useReferenceRows(entries[direction], application, direction)
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
  const testId = direction === 'usedBy' ? 'studio-used-by' : 'studio-uses'
  return (
    <Flex vertical gap="middle" data-testid="studio-dependencies">
      {directions.length > 1 && (
        <Segmented
          value={direction}
          onChange={(next) => {
            setDirection(next as Direction)
            setFilter(emptyFilter)
          }}
          options={directions.map((value) => ({
            value,
            label: t(value === 'usedBy' ? 'shell.studio.usedBy' : 'shell.studio.uses'),
          }))}
          style={{ alignSelf: 'flex-start' }}
        />
      )}
      <AxisTable<StudioChangeRow>
        testId={testId}
        dataSource={shown}
        rowKey={(row) => row.key}
        columns={columns}
        emptyText={t(direction === 'usedBy' ? 'shell.studio.usedBy.none' : 'shell.studio.uses.none')}
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
    </Flex>
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
