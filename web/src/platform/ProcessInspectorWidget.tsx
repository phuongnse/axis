import { Flex } from 'antd'
import { AxisAlert } from './AxisAlert'
import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisTable, type AxisColumn } from './AxisTable'
import { DeadLetterList } from './DeadLetterList'
import { formatValue } from './formatValue'
import {
  fetchInstances,
  forbidden,
  instanceStates,
  stateText,
  type InstanceListItem,
  type InstancePage,
} from './inspection'
import type { WidgetMetadata } from './site'
import { recordPageSize, recordQuery, tablePageSizes } from './tableQuery'
import { useTableView } from './useTableView'
import { useText } from './texts'
import { siteAddress } from './siteLinks'

interface ProcessInspectorWidgetProps {
  sitePath: string
  /** The name of the inspector page. An instance's address is under it. */
  pageName: string
  widget: WidgetMetadata
  /** The UI locale, for formatting the times. */
  locale: string
}

/** The outcome of the last finished request. */
interface LoadState {
  /** The load key, which is the request URL and the Refresh count, so a result for an older load shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: InstancePage
  failed: boolean
  forbidden: boolean
}

const pageSizes = tablePageSizes()
// useTableView needs stable arrays. These are the route's fields in column order, see docs/reference/process-inspection.md#instance-list.
const instanceFields = [
  { name: 'subjectLabel', type: 'text' },
  { name: 'state', type: 'enum' },
  { name: 'step', type: 'text' },
  { name: 'releaseNumber', type: 'integer' },
  { name: 'createdAt', type: 'date' },
  { name: 'endedAt', type: 'date' },
] as const
// The list has no parameter of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []
const createdAtField = { name: 'createdAt', type: 'date-time' } as const
const endedAtField = { name: 'endedAt', type: 'date-time' } as const

/**
 * Lists the instances of the widget's process, newest first until the view sorts them, paged on the server. The table has
 * Search, Filter on every column, Columns, a sort on every column, Refresh and paging, see
 * docs/reference/actions-and-controls.md#tables. The view lives in the URL as `search`, `filter`, `columns`, `sort`,
 * `page` and `pageSize`, and the widget sends `search`, `filter`, `sort`, `page` and `pageSize` to the instances path
 * unchanged, so the server does the work. A new view goes back to page 1. The first column links each row to the
 * instance's address under the inspector page. A `403` shows that the user may not inspect the process. Any other
 * failure shows under the toolbar, so the view can be changed to get the rows back. With nobody signed in, the
 * inspection API's 401 shows the sign-in prompt.
 *
 * The step column shows the step's name, because a list row carries no label key. Under the list, the
 * process's open dead letters have their own list, which has its own page number.
 */
export function ProcessInspectorWidget({ sitePath, pageName, widget, locale }: ProcessInspectorWidgetProps) {
  const t = useText()
  // The server sets the path on every process inspector.
  const instancesPath = widget.instancesPath!
  const instancePath = siteAddress(sitePath, pageName.toLowerCase())
  const { view: query, setView } = useTableView(instanceFields, recordPageSize, noParameters, true, true, true)
  const requestQuery = recordQuery(query)
  const requestUrl = `${instancesPath}${requestQuery ? `?${requestQuery}` : ''}`
  // Refresh counts up, so the same URL loads again.
  const [reload, setReload] = useState(0)
  const loadKey = `${reload} ${requestUrl}`
  const [load, setLoad] = useState<LoadState>({ failed: false, forbidden: false })
  const loading = load.url !== loadKey
  const failed = load.failed && !loading
  const totalCount = loading ? undefined : load.page?.totalCount

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
    fetchInstances(instancesPath, requestQuery, controller.signal)
      .then((result) =>
        setLoad(
          result === forbidden
            ? { url: loadKey, failed: false, forbidden: true }
            : { url: loadKey, page: result, failed: false, forbidden: false },
        ),
      )
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the instances of '${instancesPath}' failed`, error)
        setLoad({ url: loadKey, failed: true, forbidden: false })
      })
    return () => controller.abort()
  }, [instancesPath, requestQuery, loadKey])

  const columns = useMemo<AxisColumn<InstanceListItem>[]>(
    () => [
      {
        key: 'subjectLabel',
        kind: 'freeText',
        title: t('shell.inspector.subject'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value, instance) => (
          <Link to={`${instancePath}/${instance.id}`}>
            {instance.subjectLabel ??
              (instance.subjectId === null ? t('shell.inspector.noSubject') : t('shell.inspector.open'))}
          </Link>
        ),
      },
      {
        key: 'state',
        kind: 'fixed',
        title: t('shell.inspector.state'),
        render: (_value, instance) => stateText(t, instance.state),
        filter: {
          kind: 'enum',
          options: instanceStates.map((value) => ({ value, label: stateText(t, value) })),
        },
        sorter: true,
      },
      {
        key: 'step',
        kind: 'freeText',
        title: t('shell.inspector.step'),
        filter: { kind: 'text' },
        sorter: true,
        render: (_value, instance) => instance.step,
      },
      {
        key: 'releaseNumber',
        kind: 'fixed',
        title: t('shell.inspector.release'),
        filter: { kind: 'number' },
        sorter: true,
        render: (_value, instance) => instance.releaseNumber ?? null,
      },
      {
        key: 'createdAt',
        kind: 'fixed',
        title: t('shell.inspector.createdAt'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value, instance) => formatValue(createdAtField, instance.createdAt, {}, { locale, text: t }),
      },
      {
        key: 'endedAt',
        kind: 'fixed',
        title: t('shell.inspector.endedAt'),
        filter: { kind: 'date' },
        sorter: true,
        render: (_value, instance) =>
          instance.endedAt === null ? null : formatValue(endedAtField, instance.endedAt, {}, { locale, text: t }),
      },
    ],
    [locale, t, instancePath],
  )

  if (load.forbidden && !loading) {
    return <AxisAlert data-testid="inspector-forbidden" kind="warning" message={t('shell.inspector.forbidden')} />
  }
  return (
    <Flex vertical gap="middle">
      <AxisTable<InstanceListItem>
        rowKey={(instance) => instance.id}
        columns={columns}
        dataSource={load.page?.items}
        loading={loading}
        emptyText={t('shell.inspector.empty')}
        view={{
          query,
          onChange: setView,
          total: load.page?.totalCount ?? 0,
          pageSizes,
          search: true,
          columns: true,
        }}
        toolbar={{ onRefresh: () => setReload((count) => count + 1) }}
        failure={
          failed ? (
            <AxisAlert data-testid="inspector-error" kind="error" message={t('shell.inspector.loadFailed')} />
          ) : undefined
        }
      />
      <DeadLetterList deadLettersPath={widget.deadLettersPath!} locale={locale} />
    </Flex>
  )
}
