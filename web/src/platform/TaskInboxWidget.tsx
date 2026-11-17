import { Space, Tag } from 'antd'
import { AxisAlert } from './AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from './AxisTable'
import { formatValue } from './formatValue'
import type { WidgetMetadata } from './site'
import { recordPageSize, recordQuery, tablePageSizes } from './tableQuery'
import { fetchTasks, type TaskListItem, type TaskPage } from './tasks'
import { resolveText, useText, useTextCatalogs } from './texts'
import { useReleaseTexts } from './useReleaseTexts'
import { useSiteTableKey } from './tableChoices'
import { useTableView } from './useTableView'
import { siteAddress } from './siteLinks'

interface TaskInboxWidgetProps {
  sitePath: string
  /** The name of the inbox page. A task's address is under it. */
  pageName: string
  widget: WidgetMetadata
  /** The UI locale, for formatting the due date. */
  locale: string
  /** The site's fallback locale, for the texts of the tasks' releases. */
  fallbackLocale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The load key, which is the request URL and the Refresh count, so a result for an older load shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: TaskPage
  failed: boolean
}

const pageSizes = tablePageSizes()
// useTableView needs stable arrays. Only `subject` and `dueAt` are fields of the task list, see docs/reference/task-api.md#list-my-tasks.
// `task` is first, so Columns keeps it first.
const taskFields = [
  { name: 'task', type: 'text' },
  { name: 'subject', type: 'text' },
  { name: 'dueAt', type: 'date' },
] as const
// The list has no parameter of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []
const dueAtField = { name: 'dueAt', type: 'date-time' } as const

/**
 * Lists the signed-in user's open tasks in the task API's order until the view sorts them, paged on the server. The table
 * has Search, Filter on Subject and Due, Columns, a sort on Subject and Due, Refresh and paging, see
 * docs/reference/actions-and-controls.md#tables. The view lives in the URL as `search`, `filter`, `columns`, `sort`, `page`
 * and `pageSize`, and the widget sends `search`, `filter`, `sort`, `page` and `pageSize` to the task API unchanged, so the
 * server does the work. A new view goes back to page 1. Each row links to the task's address under the inbox page. Task
 * stays first, with no sort or filter, because its label is resolved in the browser. With no sort, rows keep the task API's
 * order: due date first, with no due date last. A failed load shows under the toolbar, so the view can be changed to get the
 * rows back. With nobody signed in, the task API's 401 shows the sign-in prompt.
 *
 * An overdue task shows an "Overdue" tag beside its due date, and a task the user claimed shows a
 * "Claimed by you" tag beside its label.
 *
 * A row's label comes from the texts of the release its task started on, then the site texts and
 * the platform texts. The table stays loading until the texts of every release on the page have
 * loaded. If they fail to load, the inbox shows its load error.
 */
export function TaskInboxWidget({ sitePath, pageName, widget, locale, fallbackLocale }: TaskInboxWidgetProps) {
  const t = useText()
  const { catalogs, development } = useTextCatalogs()
  // The server sets the path on every task inbox.
  const tasksPath = widget.tasksPath!
  const taskPath = siteAddress(sitePath, pageName.toLowerCase())
  const {
    view: query,
    setView,
    saved,
  } = useTableView(taskFields, recordPageSize, noParameters, true, true, true, '', useSiteTableKey(sitePath, pageName))
  const requestQuery = recordQuery(query)
  const requestUrl = `${tasksPath}${requestQuery ? `?${requestQuery}` : ''}`
  // Refresh counts up, so the same URL loads again.
  const [reload, setReload] = useState(0)
  const loadKey = `${reload} ${requestUrl}`
  const [state, setState] = useState<LoadState>({ failed: false })
  const loading = state.url !== loadKey
  const failed = state.failed && !loading
  const totalCount = loading ? undefined : state.page?.totalCount
  const items = state.page?.items
  const releaseIds = useMemo(() => items?.map((task) => task.releaseId) ?? [], [items])
  const releaseTexts = useReleaseTexts(sitePath, releaseIds, locale, fallbackLocale)
  const releaseCatalogs = releaseTexts.catalogs
  const textsLoading = releaseIds.some((id) => !releaseCatalogs.has(id))

  // A page past the end becomes the last page once the total is known, so the URL and the
  // pagination agree.
  useEffect(() => {
    if (totalCount === undefined) {
      return
    }
    const lastPage = Math.max(1, Math.ceil(totalCount / query.pageSize))
    if (query.page > lastPage) {
      setView({ ...query, page: lastPage }, { replace: true })
    }
  }, [totalCount, query, setView])

  useEffect(() => {
    const controller = new AbortController()
    fetchTasks(tasksPath, requestQuery, controller.signal)
      .then((page) => setState({ url: loadKey, page, failed: false }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the tasks of '${tasksPath}' failed`, error)
        setState({ url: loadKey, failed: true })
      })
    return () => controller.abort()
  }, [tasksPath, requestQuery, loadKey])

  const columns = useMemo<AxisColumn<TaskListItem>[]>(
    () => [
      {
        key: 'task',
        kind: 'freeText',
        title: t('shell.tasks.task'),
        render: (_value: unknown, task: TaskListItem) => {
          const releaseCatalog = releaseCatalogs.get(task.releaseId)
          const label = releaseCatalog
            ? resolveText(task.labelKey, [releaseCatalog, ...catalogs], development)
            : t(task.labelKey)
          // The task list returns a claimed task only to its claimer, so a claimer here is the signed-in user.
          return (
            <Space size="small">
              <Link to={`${taskPath}/${task.id}`}>{label}</Link>
              {/* oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token. */}
              {task.claimedBy !== null && <Tag color="blue">{t('shell.tasks.claimedByYou')}</Tag>}
            </Space>
          )
        },
      },
      {
        key: 'subject',
        kind: 'freeText',
        title: t('shell.tasks.subject'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value: unknown, task: TaskListItem) => task.subject.label ?? null,
      },
      {
        key: 'dueAt',
        kind: 'fixed',
        title: t('shell.tasks.dueAt'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value: unknown, task: TaskListItem) =>
          task.dueAt === null ? null : (
            <Space size="small">
              {formatValue(dueAtField, task.dueAt, {}, { locale, text: t })}
              {/* oxlint-disable-next-line axis/no-preset-tag-colour -- Predates the colour tokens; colour it with a token. */}
              {task.overdue && <Tag color="error">{t('shell.tasks.overdue')}</Tag>}
            </Space>
          ),
      },
    ],
    [locale, t, taskPath, releaseCatalogs, catalogs, development],
  )

  return (
    <AxisTable<TaskListItem>
      rowKey={(task) => task.id}
      columns={columns}
      dataSource={items}
      loading={loading || textsLoading}
      emptyText={t('shell.tasks.empty')}
      view={{
        query,
        onChange: setView,
        saved,
        total: state.page?.totalCount ?? 0,
        pageSizes,
        search: true,
        columns: true,
      }}
      toolbar={{ onRefresh: () => setReload((count) => count + 1) }}
      failure={
        failed || releaseTexts.failed ? (
          <AxisAlert data-testid="tasks-error" kind="error" message={t('shell.tasks.loadFailed')} />
        ) : undefined
      }
    />
  )
}
