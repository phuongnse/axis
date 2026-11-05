import { Alert, Flex, Typography } from 'antd'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { useContext, useEffect, useState, type HTMLAttributes } from 'react'
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
 * The Uses and Used by lists of a selected resource, each entry a name that opens it. A load failure shows an alert in
 * their place.
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
  // Proposed: Uses and Used by are two panels, as the form's sections are, each with its entries in groups by kind: the other resource by its label as the link that opens it, and how the one uses the other, in words. Uses leaves out
  // what the kind's form already shows, its labels' texts on every kind, and Used by is left out for a kind nothing can
  // refer to, as it never applies; a panel with nothing left to show is left out. The same resource used the same way
  // shows once.
  const shown = usesShownByForm[kind] ?? new Set(['text'])
  const uses = references.uses.filter(
    (entry) => !shown.has(entry.node.type === 'resource' ? entry.node.kind : entry.node.type),
  )
  const showUses = !(usesShownByForm[kind] && uses.length === 0)
  const showUsedBy = !neverUsed.has(kind)
  if (!showUses && !showUsedBy) {
    return null
  }
  return (
    <Flex vertical gap="middle">
      {showUses && (
        <ReferencePanel
          testId="studio-uses"
          title={t('shell.studio.uses')}
          entries={uses}
          application={application}
          direction="uses"
        />
      )}
      {showUsedBy && (
        <ReferencePanel
          testId="studio-used-by"
          title={t('shell.studio.usedBy')}
          entries={references.usedBy}
          application={application}
          direction="usedBy"
        />
      )}
    </Flex>
  )
}

function ReferencePanel({
  testId,
  title,
  entries,
  application,
  direction,
}: {
  testId: string
  title: string
  entries: StudioReference[]
  application: string
  direction: Direction
}) {
  const t = useText()
  const { colour, radius, spacing } = useDesignTokens()
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
      rows.push({ key, name: node.key, kind: '', change: relation })
    }
  }
  return (
    <section
      data-testid={testId}
      style={{
        background: colour.surface,
        border: `1px solid ${colour.borderSubtle}`,
        borderRadius: radius.base,
        padding: spacing.lg,
      }}
    >
      <Flex vertical gap={spacing.md}>
        <Typography.Title level={5} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
        {rows.length === 0 ? <EmptyState /> : <ReferenceGroups testId={`${testId}-list`} rows={rows} />}
      </Flex>
    </section>
  )
}

/** A row of a Uses or Used by table: a kind's group row, or an entry under it. */
type ReferenceRow = { key: string; group: string } | { key: string; group?: undefined; entry: StudioChangeRow }

/**
 * Proposed: the entries of a Uses or Used by panel as the standard table, `AxisTable`, grouped by kind in the sidebar's
 * order: a group row with the kind's name, whose other cells stay blank, then its entries, each the other resource by
 * its label, the link that opens it, and how the one uses the other. The kind is the group's, so no row repeats it.
 */
function ReferenceGroups({ rows, testId }: { rows: readonly StudioChangeRow[]; testId: string }) {
  const t = useText()
  const { colour } = useDesignTokens()
  const collator = nameCollator()
  const table: ReferenceRow[] = []
  let current: string | null = null
  for (const row of [...rows].sort((a, b) => (a.order ?? 999) - (b.order ?? 999) || collator.compare(a.name, b.name))) {
    const kind = row.kind || t('shell.studio.references.textKeys')
    if (kind !== current) {
      table.push({ key: `group/${kind}`, group: kind })
      current = kind
    }
    table.push({ key: row.key, entry: row })
  }
  const columns: AxisColumn<ReferenceRow>[] = [
    {
      key: 'name',
      kind: 'freeText',
      title: t('shell.studio.references.name'),
      render: (_, row) =>
        row.group !== undefined ? (
          <Typography.Text strong>{row.group}</Typography.Text>
        ) : row.entry.to ? (
          <Link to={row.entry.to} style={{ color: colour.primaryText }}>
            {row.entry.name}
          </Link>
        ) : (
          row.entry.name
        ),
    },
    {
      key: 'how',
      kind: 'freeText',
      title: t('shell.studio.references.how'),
      render: (_, row) => (row.group !== undefined ? null : row.entry.change),
    },
  ]
  return (
    <AxisTable<ReferenceRow>
      testId={testId}
      dataSource={table}
      rowKey={(row) => row.key}
      columns={columns}
      emptyText=""
      pagination={false}
      blankRow={(row) => row.group !== undefined}
      rowAttributes={(row) =>
        (row.group !== undefined
          ? { 'data-testid': `${testId}-group`, style: { background: colour.canvas } }
          : { 'data-testid': `${testId}-row` }) as unknown as HTMLAttributes<HTMLElement>
      }
    />
  )
}
