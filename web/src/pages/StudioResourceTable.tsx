import { Alert, Flex, Tag, Typography } from 'antd'
import { useContext, useEffect, useMemo, useState, type MouseEvent, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { AxisTable, searchRows, type AxisColumn } from '../platform/AxisTable'
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
import {
  matchesFilter,
  parseFilter,
  TableFilterButton,
  TableFilterChips,
  type FilterField,
  type FilterGroup,
} from '../platform/TableFilter'
import { chosenColumns, parseColumnChoice, TableColumnsButton, writeColumnChoice } from '../platform/TableColumns'
import { pageSizes, recordPageSize } from '../platform/tableQuery'
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
  status: StudioResourceActivity['status'] | null
}

// Proposed: a status tag's words take the text tokens, as the tag's own colours fall under 4.5:1 in light mode.
/** Proposed: the order the Status column sorts in: what needs a publish first. */
const statusOrder = { new: 0, changed: 1, published: 2 } as const

const statusColours = {
  new: { tag: 'blue', text: 'infoText' },
  changed: { tag: 'gold', text: 'warningText' },
  published: { tag: 'green', text: 'successText' },
} as const

/**
 * Proposed: the list of one kind, the same five columns for every kind: Name, a real link that opens the resource,
 * with a neutral tag of its module on a module's resource, Description, Status, Last modified by and Last modified at. A column always shows, and a cell with no value
 * shows the dash. A set sorts by name (D132), and Name and Status sort by their header. Search, sort and page live in the
 * address. The status comes from the resource activity route, loaded apart from the
 * list, so the rows show and open at once: while it loads the cell says so, and when it fails it says it is unavailable
 * rather than empty.
 */
export function StudioResourceTable({
  application,
  folderRevision,
  items,
  extra,
  searchText,
  search,
  main,
  addressOf,
  onOpen,
  onRefresh,
}: {
  application: string
  /** Goes up after a write, which loads the activity again. */
  folderRevision: number
  items: readonly StudioResourceItem[]
  extra: readonly { id: string; name: string; module: string | null }[]
  /** The text the search box holds. */
  searchText: string
  search: ReactNode
  main: ReactNode
  addressOf: (id: string) => string
  onOpen: (id: string) => void
  /** Loads the list and the activity again. */
  onRefresh: () => void
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { colour, spacing } = useDesignTokens()
  const { lost } = useStudioConnection()
  const [catalog, setCatalog] = useState<StudioCatalog>()
  const [params, setParams] = useSearchParams()
  const sort = params.get('sort')
  const sortKey = sort?.replace(/^-/, '')
  const descending = sort?.startsWith('-') ?? false
  const pageSizeParam = Number(params.get('pageSize'))
  const pageSize = pageSizes.includes(pageSizeParam) ? pageSizeParam : recordPageSize
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
      status: activity?.get(item.id)?.status ?? null,
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
        status: entry?.status ?? null,
      }
    })
    const byName = (a: ResourceRow, b: ResourceRow) => collator.compare(a.name, b.name)
    const rank = (row: ResourceRow) => (row.status === null ? 3 : statusOrder[row.status])
    const compare =
      sortKey === 'status' ? (a: ResourceRow, b: ResourceRow) => rank(a) - rank(b) || byName(a, b) : byName
    const sorted = [...own, ...fromModules].sort(compare)
    return descending ? sorted.reverse() : sorted
  }, [items, extra, locale, activity, sortKey, descending])

  const unavailable = (
    <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.list.unavailable')}</Typography.Text>
  )
  const loading = (
    <Typography.Text style={{ color: colour.textMuted }}>{t('shell.studio.list.loading')}</Typography.Text>
  )
  const sortOrderOf = (key: string) =>
    sortKey !== key ? undefined : descending ? ('descend' as const) : ('ascend' as const)
  const moduleLabel = (row: ResourceRow) =>
    row.module === null ? null : (catalog?.modules.find((module) => module.id === row.module)?.label ?? row.module)
  const statusLabel = (row: ResourceRow) => (row.status ? t(`shell.studio.list.status.${row.status}`) : null)
  const filter = parseFilter(params.get('filter'))
  const applyFilter = (next: FilterGroup) =>
    setParams(
      (current) => {
        const query = new URLSearchParams(current)
        query.delete('page')
        if (next.items.length === 0) {
          query.delete('filter')
        } else {
          query.set('filter', JSON.stringify(next))
        }
        return query
      },
      { replace: true },
    )
  const moduleIds = [...new Set(extra.flatMap((item) => (item.module === null ? [] : [item.module])))]
  const filterFields: FilterField[] = [
    { key: 'name', label: t('shell.studio.list.name'), type: 'text' },
    { key: 'description', label: t('shell.studio.list.description'), type: 'text' },
    {
      key: 'module',
      label: t('shell.studio.list.module'),
      type: 'enum',
      options: moduleIds.map((id) => ({
        value: id,
        label: catalog?.modules.find((module) => module.id === id)?.label ?? id,
      })),
    },
    {
      key: 'status',
      label: t('shell.studio.list.status'),
      type: 'enum',
      options: (['new', 'changed', 'published'] as const).map((status) => ({
        value: status,
        label: t(`shell.studio.list.status.${status}`),
      })),
    },
    { key: 'lastModifiedBy', label: t('shell.studio.list.lastModifiedBy'), type: 'text' },
    { key: 'lastModifiedAt', label: t('shell.studio.list.lastModifiedAt'), type: 'date' },
  ]
  const read = (row: ResourceRow) => (field: string) => {
    switch (field) {
      case 'name':
        return row.name
      case 'description':
        return row.description
      case 'module':
        return row.module
      case 'status':
        return row.status
      default:
        return null
    }
  }
  const columns: AxisColumn<ResourceRow>[] = [
    {
      key: 'name',
      kind: 'text',
      title: t('shell.studio.list.name'),
      searchText: (row) => row.name,
      sorter: true,
      sortOrder: sortOrderOf('name'),
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
      searchText: (row) => [row.description, moduleLabel(row)].filter(Boolean).join(' '),
      render: (row) => row.description ?? <EmptyDash />,
    },
    {
      key: 'status',
      kind: 'fit',
      title: t('shell.studio.list.status'),
      searchText: statusLabel,
      sorter: true,
      sortOrder: sortOrderOf('status'),
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
    {
      key: 'lastModifiedBy',
      kind: 'fit',
      title: t('shell.studio.list.lastModifiedBy'),
      sorter: true,
      sortOrder: sortOrderOf('lastModifiedBy'),
      render: () => <EmptyDash />,
    },
    {
      key: 'lastModifiedAt',
      kind: 'fit',
      title: t('shell.studio.list.lastModifiedAt'),
      sorter: true,
      sortOrder: sortOrderOf('lastModifiedAt'),
      render: () => <EmptyDash />,
    },
  ]

  const columnChoice = parseColumnChoice(
    params.get('columns'),
    columns.map((column) => column.key),
  )
  const shown = searchRows(rows, columns, searchText).filter((row) => matchesFilter(filter, read(row)))
  const lastPage = Math.max(1, Math.ceil(shown.length / pageSize))
  const page = Math.min(Math.max(1, Number(params.get('page')) || 1), lastPage)

  return (
    <AxisTable<ResourceRow>
      testId="studio-resource-table"
      rows={shown}
      rowKey={(row) => row.id}
      columns={chosenColumns(columns, columnChoice)}
      toolbar={{
        view: (
          <Flex gap="small" align="center">
            {search}
            <TableFilterButton fields={filterFields} filter={filter} onApply={applyFilter} />
          </Flex>
        ),
        columns: (
          <TableColumnsButton
            columns={columns.map((column) => ({ key: column.key, label: String(column.title) }))}
            choice={columnChoice}
            onApply={(choice) =>
              setParams(
                (current) => {
                  const query = new URLSearchParams(current)
                  if (choice === null) {
                    query.delete('columns')
                  } else {
                    query.set('columns', writeColumnChoice(choice))
                  }
                  return query
                },
                { replace: true },
              )
            }
          />
        ),
        refresh: onRefresh,
        main,
      }}
      above={
        <>
          {activity === null && (
            <Alert
              type="warning"
              data-testid="studio-resource-activity-error"
              title={t('shell.studio.list.activityFailed')}
            />
          )}
          <TableFilterChips fields={filterFields} filter={filter} onChange={applyFilter} />
        </>
      }
      emptyText={t('shell.studio.noResources')}
      pagination={{ current: page, pageSize, pageSizeOptions: pageSizes, showSizeChanger: true }}
      onChange={(next, _filters, sorter) => {
        const column = Array.isArray(sorter) ? sorter[0] : sorter
        setParams(
          (current) => {
            const query = new URLSearchParams(current)
            const write = (name: string, value: string | null) =>
              value === null ? query.delete(name) : query.set(name, value)
            write('sort', column.order ? `${column.order === 'descend' ? '-' : ''}${String(column.columnKey)}` : null)
            write('page', next.current && next.current > 1 ? String(next.current) : null)
            write('pageSize', next.pageSize === recordPageSize ? null : String(next.pageSize))
            return query
          },
          { replace: true },
        )
      }}
    />
  )
}
