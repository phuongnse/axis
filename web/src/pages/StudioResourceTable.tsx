import { Alert, Tag, Typography } from 'antd'
import { useContext, useEffect, useMemo, useState, type MouseEvent, type ReactNode } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import { EmptyDash } from '../platform/EmptyValue'
import {
  fetchStudioCatalog,
  fetchStudioResourceActivity,
  resourceDescription,
  resourceTitle,
  ServerUnreachableError,
  type StudioCatalog,
  type StudioResourceActivity,
  type StudioResourceItem,
} from '../platform/studio'
import { useStudioConnection } from '../platform/studioConnection'
import { useText } from '../platform/texts'
import { useDesignTokens } from '../platform/themeMode'
import { PlatformContext } from './context'
import { nameCollator } from './studioViews'

/** One row: a resource of the folder, or one of a module, which has a status like any other. */
interface ResourceRow {
  id: string
  name: string
  description: string | null
  module: string | null
}

// Proposed: a status tag's words take the text tokens, as the tag's own colours fall under 4.5:1 in light mode.
const statusColours = {
  new: { tag: 'blue', text: 'infoText' },
  changed: { tag: 'gold', text: 'warningText' },
  published: { tag: 'green', text: 'successText' },
} as const

/**
 * Proposed: the list of one kind, the same five columns for every kind: Name, a real link that opens the resource,
 * with a neutral tag of its module on a module's resource, Description, Status, Last modified by and Last modified at. A column always shows, and a cell with no value
 * shows the dash. A set sorts by name (D132). The status comes from the resource activity route, loaded apart from the
 * list, so the rows show and open at once: while it loads the cell says so, and when it fails it says it is unavailable
 * rather than empty.
 */
export function StudioResourceTable({
  application,
  folderRevision,
  items,
  extra,
  search,
  main,
  addressOf,
  onOpen,
}: {
  application: string
  /** Goes up after a write, which loads the activity again. */
  folderRevision: number
  items: readonly StudioResourceItem[]
  extra: readonly { id: string; name: string; module: string | null }[]
  search: ReactNode
  main: ReactNode
  addressOf: (id: string) => string
  onOpen: (id: string) => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, spacing } = useDesignTokens()
  const { lost } = useStudioConnection()
  const [catalog, setCatalog] = useState<StudioCatalog>()
  // What the route answered for an application, or null when it failed. After a write the rows stay until the new answer.
  const [loaded, setLoaded] = useState<{
    application: string
    activity: ReadonlyMap<string, StudioResourceActivity> | null
  }>()
  const activity = loaded?.application === application ? loaded.activity : undefined

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioResourceActivity(application, controller.signal).then(
      (rows) => setLoaded({ application, activity: new Map(rows.map((row) => [row.id, row])) }),
      (error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        if (error instanceof ServerUnreachableError) {
          lost()
          return
        }
        console.warn('Loading the resource activity failed', error)
        setLoaded({ application, activity: null })
      },
    )
    return () => controller.abort()
  }, [application, folderRevision, lost])

  // A module's tag says its label, so the catalog loads when the list holds a module's resources.
  const hasModules = extra.length > 0
  useEffect(() => {
    if (!hasModules) {
      return
    }
    const controller = new AbortController()
    fetchStudioCatalog(controller.signal).then(setCatalog, (error: unknown) => {
      if (!controller.signal.aborted) {
        console.warn('Loading the Studio catalog failed', error)
      }
    })
    return () => controller.abort()
  }, [hasModules])

  const rows = useMemo(() => {
    const collator = nameCollator()
    const own = items.map((item): ResourceRow => ({
      id: item.id,
      name: resourceTitle(item, locale),
      description: resourceDescription(item, locale),
      module: null,
    }))
    // A module's resource is named `<module>/<name>`. A person reads its label, from the activity, or else the name.
    const fromModules = extra.map((item): ResourceRow => {
      const entry = activity?.get(item.id)
      const name =
        item.module !== null && item.name.startsWith(`${item.module}/`)
          ? item.name.slice(item.module.length + 1)
          : item.name
      return {
        ...item,
        name: entry?.titles?.[locale] ?? entry?.titles?.en ?? name,
        description: entry ? resourceDescription(entry, locale) : null,
      }
    })
    return [...own, ...fromModules].sort((a, b) => collator.compare(a.name, b.name))
  }, [items, extra, locale, activity])

  const unavailable = (
    <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.list.unavailable')}</Typography.Text>
  )
  const loading = (
    <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.list.loading')}</Typography.Text>
  )
  const columns: AxisColumn<ResourceRow>[] = [
    {
      key: 'name',
      kind: 'text',
      title: t('shell.studio.list.name'),
      render: (row) => (
        <>
          <Link
            to={addressOf(row.id)}
            data-testid={`studio-resource-${row.id}`}
            style={{ color: colour.primaryText }}
            onClick={(event: MouseEvent) => {
              // A plain click stays in the workspace; Ctrl+click opens the address in a new tab.
              if (!event.ctrlKey && !event.metaKey && !event.shiftKey) {
                event.preventDefault()
                onOpen(row.id)
              }
            }}
          >
            {row.name}
          </Link>
          {row.module !== null && (
            <Tag style={{ marginInlineStart: spacing.xs }} data-testid={`studio-resource-module-${row.id}`}>
              {catalog?.modules.find((module) => module.id === row.module)?.label ?? row.module}
            </Tag>
          )}
        </>
      ),
    },
    {
      key: 'description',
      kind: 'text',
      title: t('shell.studio.list.description'),
      render: (row) => row.description ?? <EmptyDash />,
    },
    {
      key: 'status',
      kind: 'fit',
      title: t('shell.studio.list.status'),
      render: (row) => {
        if (activity === undefined) {
          return loading
        }
        if (activity === null) {
          return unavailable
        }
        const status = activity.get(row.id)?.status
        return status ? (
          <Tag color={statusColours[status].tag} style={{ color: colour[statusColours[status].text] }}>
            {t(`shell.studio.list.status.${status}`)}
          </Tag>
        ) : (
          <EmptyDash />
        )
      },
    },
    // Nothing records who last changed a resource yet, so these two columns show the dash until that logic is built.
    { key: 'lastModifiedBy', kind: 'fit', title: t('shell.studio.list.lastModifiedBy'), render: () => <EmptyDash /> },
    { key: 'lastModifiedAt', kind: 'fit', title: t('shell.studio.list.lastModifiedAt'), render: () => <EmptyDash /> },
  ]

  return (
    <AxisTable<ResourceRow>
      testId="studio-resource-table"
      rows={rows}
      rowKey={(row) => row.id}
      columns={columns}
      toolbar={{ view: search, main }}
      above={
        activity === null ? (
          <Alert
            type="warning"
            data-testid="studio-resource-activity-error"
            title={t('shell.studio.list.activityFailed')}
          />
        ) : undefined
      }
      emptyText={t('shell.studio.noResources')}
      pagination={{ pageSize: 20, hideOnSinglePage: true, showSizeChanger: false }}
    />
  )
}
