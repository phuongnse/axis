import { Alert, Flex, Typography } from 'antd'
import { Link } from 'react-router'
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

/**
 * Proposed: the entries of a Uses or Used by panel in groups by kind, in the sidebar's order, each under the kind's name,
 * so a resource used in many places reads by kind rather than as one long list. A row is the
 * other resource by its label, the link that opens it, then how the one uses the other in a few muted words. The kind
 * is the group's, so no row repeats it.
 */
function ReferenceGroups({ rows, testId }: { rows: readonly StudioChangeRow[]; testId: string }) {
  const t = useText()
  const { colour, spacing } = useDesignTokens()
  const collator = nameCollator()
  const groups = new Map<string, StudioChangeRow[]>()
  for (const row of [...rows].sort((a, b) => (a.order ?? 999) - (b.order ?? 999) || collator.compare(a.name, b.name))) {
    const kind = row.kind || t('shell.studio.references.textKeys')
    groups.set(kind, [...(groups.get(kind) ?? []), row])
  }
  return (
    <Flex vertical gap={spacing.md} data-testid={testId}>
      {[...groups].map(([kind, members]) => (
        <section key={kind} data-testid={`${testId}-group`}>
          <Typography.Text style={{ display: 'block', color: colour.textMuted, marginBottom: spacing.xs }}>
            {kind}
          </Typography.Text>
          <ul
            style={{
              margin: 0,
              padding: 0,
              listStyle: 'none',
              display: 'grid',
              gridTemplateColumns: 'fit-content(320px) max-content',
              columnGap: spacing.lg,
              rowGap: spacing.xs,
              alignItems: 'baseline',
            }}
          >
            {members.map((row) => (
              <li key={row.key} style={{ display: 'contents' }} data-testid={`${testId}-row`}>
                {row.to ? (
                  <Link to={row.to} style={{ color: colour.primaryText }}>
                    {row.name}
                  </Link>
                ) : (
                  <Typography.Text>{row.name}</Typography.Text>
                )}
                <Typography.Text style={{ color: colour.textMuted }}>{row.change}</Typography.Text>
              </li>
            ))}
          </ul>
        </section>
      ))}
    </Flex>
  )
}
