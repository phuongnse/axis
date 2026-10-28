import { Alert, Space, Table, Tag, type TableColumnsType, type TableProps } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import { formatValue } from './formatValue'
import { markCurrentPage } from './pagination'
import type { WidgetMetadata } from './site'
import { parseTableQuery, recordQuery, tablePageSizes, writeTableQuery } from './tableQuery'
import { fetchTasks, type TaskListItem, type TaskPage } from './tasks'
import { resolveText, useText, useTextCatalogs } from './texts'
import { useReleaseTexts } from './useReleaseTexts'

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
  /** The request URL, so a result for an older request shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: TaskPage
  failed: boolean
}

const pageSizes = tablePageSizes()
const dueAtField = { name: 'dueAt', type: 'date-time' } as const

/**
 * Lists the signed-in user's open tasks in the application, in the task API's order: due date
 * first, with no due date last. Each row links to the task's address under the inbox page. Paging
 * lives in the URL as the task API's `page` and `pageSize`, as on a table. With nobody signed in, the
 * task API's 401 shows the sign-in prompt.
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
  const [searchParams, setSearchParams] = useSearchParams()
  // The server sets the path on every task inbox.
  const tasksPath = widget.tasksPath!
  const taskPath = `/${sitePath}/${pageName.toLowerCase()}`
  // The inbox has no sort, so no field is offered as one.
  const query = useMemo(() => parseTableQuery(searchParams, []), [searchParams])
  const requestQuery = recordQuery(query)
  const requestUrl = `${tasksPath}?${requestQuery}`
  const [state, setState] = useState<LoadState>({ failed: false })
  const loading = state.url !== requestUrl
  const failed = state.failed && !loading
  const totalCount = loading ? undefined : state.page?.totalCount
  const items = state.page?.items
  const releaseIds = useMemo(() => items?.map((task) => task.releaseId) ?? [], [items])
  const releaseTexts = useReleaseTexts(sitePath, releaseIds, locale, fallbackLocale)
  const releaseCatalogs = releaseTexts.catalogs
  const textsLoading = releaseIds.some((id) => !releaseCatalogs.has(id))

  // An invalid, repeated or out-of-order parameter is rewritten without a history entry.
  useEffect(() => {
    const canonical = writeTableQuery(searchParams, query)
    if (canonical.toString() !== searchParams.toString()) {
      setSearchParams(canonical, { replace: true })
    }
  }, [searchParams, query, setSearchParams])

  // A page past the end becomes the last page once the total is known, so the URL and the
  // pagination agree.
  useEffect(() => {
    if (totalCount === undefined) {
      return
    }
    const lastPage = Math.max(1, Math.ceil(totalCount / query.pageSize))
    if (query.page > lastPage) {
      setSearchParams(writeTableQuery(searchParams, { ...query, page: lastPage }), { replace: true })
    }
  }, [totalCount, searchParams, query, setSearchParams])

  useEffect(() => {
    const controller = new AbortController()
    const url = `${tasksPath}?${requestQuery}`
    fetchTasks(tasksPath, requestQuery, controller.signal)
      .then((page) => setState({ url, page, failed: false }))
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the tasks of '${tasksPath}' failed`, error)
        setState({ url, failed: true })
      })
    return () => controller.abort()
  }, [tasksPath, requestQuery])

  const columns = useMemo<TableColumnsType<TaskListItem>>(
    () => [
      {
        key: 'task',
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
              {task.claimedBy !== null && <Tag color="blue">{t('shell.tasks.claimedByYou')}</Tag>}
            </Space>
          )
        },
      },
      {
        key: 'subject',
        title: t('shell.tasks.subject'),
        render: (_value: unknown, task: TaskListItem) => task.subject.label ?? '',
      },
      {
        key: 'dueAt',
        title: t('shell.tasks.dueAt'),
        render: (_value: unknown, task: TaskListItem) => (
          <Space size="small">
            {formatValue(dueAtField, task.dueAt, {}, { locale, text: t })}
            {task.overdue && <Tag color="error">{t('shell.tasks.overdue')}</Tag>}
          </Space>
        ),
      },
    ],
    [locale, t, taskPath, releaseCatalogs, catalogs, development],
  )

  // A new page size starts again at page 1. Paging adds history entries.
  const onChange: TableProps<TaskListItem>['onChange'] = (pagination) => {
    const pageSize = pagination.pageSize ?? query.pageSize
    const page = pageSize !== query.pageSize ? 1 : (pagination.current ?? 1)
    setSearchParams(writeTableQuery(searchParams, { page, pageSize, sort: null }))
  }

  if (failed || releaseTexts.failed) {
    return <Alert data-testid="tasks-error" type="error" title={t('shell.tasks.loadFailed')} />
  }
  return (
    <div aria-busy={loading || textsLoading}>
      <Table<TaskListItem>
        rowKey="id"
        columns={columns}
        scroll={{ x: 'max-content' }}
        dataSource={state.page?.items}
        loading={loading || textsLoading}
        locale={{ emptyText: t('shell.tasks.empty') }}
        pagination={{
          current: query.page,
          pageSize: query.pageSize,
          total: state.page?.totalCount ?? 0,
          pageSizeOptions: pageSizes,
          showSizeChanger: true,
          itemRender: markCurrentPage(query.page),
        }}
        onChange={onChange}
      />
    </div>
  )
}
