import { Alert, Flex, Select, Table, type TableColumnsType, type TableProps } from 'antd'
import { useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
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
import { parseTableQuery, recordQuery, tablePageSizes, writeTableQuery } from './tableQuery'
import { useText } from './texts'

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
  /** The request URL, so a result for an older request shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: InstancePage
  failed: boolean
  forbidden: boolean
}

const pageSizes = tablePageSizes()
const parameterNames = ['state']
const createdAtField = { name: 'createdAt', type: 'date-time' } as const
const endedAtField = { name: 'endedAt', type: 'date-time' } as const

/**
 * Lists the instances of the widget's process, newest first, as the inspection API orders them. A
 * clearable select filters them by state. `state`, `page` and `pageSize` live in the URL, and a
 * state change goes back to page 1. Each row links to the instance's address under the inspector
 * page. A `403` shows that the user may not inspect the process. With nobody signed in, the
 * inspection API's 401 starts sign-in.
 *
 * The step column shows the step's name, because a list row carries no label key.
 */
export function ProcessInspectorWidget({ sitePath, pageName, widget, locale }: ProcessInspectorWidgetProps) {
  const t = useText()
  const [searchParams, setSearchParams] = useSearchParams()
  // The server sets the path on every process inspector.
  const instancesPath = widget.instancesPath!
  const instancePath = `/${sitePath}/${pageName.toLowerCase()}`
  // The list has no sort, so no field is offered as one. A state the API does not know is dropped,
  // so the server never answers it with a 400.
  const query = useMemo(() => {
    const parsed = parseTableQuery(searchParams, [], undefined, parameterNames)
    const state = parsed.parameters?.state
    return state === undefined || (instanceStates as readonly string[]).includes(state)
      ? parsed
      : { ...parsed, parameters: {} }
  }, [searchParams])
  const state = query.parameters?.state
  const requestQuery = recordQuery(query)
  const requestUrl = `${instancesPath}${requestQuery ? `?${requestQuery}` : ''}`
  const [load, setLoad] = useState<LoadState>({ failed: false, forbidden: false })
  const loading = load.url !== requestUrl
  const failed = load.failed && !loading
  const totalCount = loading ? undefined : load.page?.totalCount

  // An invalid, repeated or out-of-order parameter is rewritten without a history entry.
  useEffect(() => {
    const canonical = writeTableQuery(searchParams, query, undefined, parameterNames)
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
      setSearchParams(writeTableQuery(searchParams, { ...query, page: lastPage }, undefined, parameterNames), {
        replace: true,
      })
    }
  }, [totalCount, searchParams, query, setSearchParams])

  useEffect(() => {
    const controller = new AbortController()
    fetchInstances(instancesPath, requestQuery, controller.signal)
      .then((result) =>
        setLoad(
          result === forbidden
            ? { url: requestUrl, failed: false, forbidden: true }
            : { url: requestUrl, page: result, failed: false, forbidden: false },
        ),
      )
      .catch((error: unknown) => {
        if (controller.signal.aborted) {
          return
        }
        console.warn(`Loading the instances of '${instancesPath}' failed`, error)
        setLoad({ url: requestUrl, failed: true, forbidden: false })
      })
    return () => controller.abort()
  }, [instancesPath, requestQuery, requestUrl])

  const columns = useMemo<TableColumnsType<InstanceListItem>>(
    () => [
      {
        key: 'state',
        title: t('shell.inspector.state'),
        render: (_value: unknown, instance: InstanceListItem) => stateText(t, instance.state),
      },
      { key: 'step', title: t('shell.inspector.step'), render: (_value: unknown, instance) => instance.step },
      {
        key: 'subject',
        title: t('shell.inspector.subject'),
        render: (_value: unknown, instance) => instance.subjectId,
      },
      {
        key: 'release',
        title: t('shell.inspector.release'),
        render: (_value: unknown, instance) => instance.releaseId,
      },
      {
        key: 'createdAt',
        title: t('shell.inspector.createdAt'),
        render: (_value: unknown, instance) => formatValue(createdAtField, instance.createdAt, {}, { locale, text: t }),
      },
      {
        key: 'endedAt',
        title: t('shell.inspector.endedAt'),
        render: (_value: unknown, instance) => formatValue(endedAtField, instance.endedAt, {}, { locale, text: t }),
      },
      {
        key: 'open',
        render: (_value: unknown, instance) => (
          <Link to={`${instancePath}/${instance.id}`}>{t('shell.inspector.open')}</Link>
        ),
      },
    ],
    [locale, t, instancePath],
  )

  const stateOptions = useMemo(() => instanceStates.map((value) => ({ value, label: stateText(t, value) })), [t])

  // A new state or page size starts again at page 1. Paging adds history entries.
  const onChange: TableProps<InstanceListItem>['onChange'] = (pagination) => {
    const pageSize = pagination.pageSize ?? query.pageSize
    const page = pageSize !== query.pageSize ? 1 : (pagination.current ?? 1)
    setSearchParams(writeTableQuery(searchParams, { ...query, page, pageSize }, undefined, parameterNames))
  }

  const onStateChange = (value: string | undefined) =>
    setSearchParams(
      writeTableQuery(
        searchParams,
        { ...query, page: 1, parameters: value ? { state: value } : {} },
        undefined,
        parameterNames,
      ),
    )

  if (load.forbidden && !loading) {
    return <Alert data-testid="inspector-forbidden" type="warning" message={t('shell.inspector.forbidden')} />
  }
  if (failed) {
    return <Alert data-testid="inspector-error" type="error" message={t('shell.inspector.loadFailed')} />
  }
  return (
    <Flex vertical gap="middle">
      <Select
        allowClear
        aria-label={t('shell.inspector.state')}
        style={{ width: 240 }}
        placeholder={t('shell.inspector.allStates')}
        value={state}
        options={stateOptions}
        onChange={onStateChange}
      />
      <Table<InstanceListItem>
        rowKey="id"
        columns={columns}
        dataSource={load.page?.items}
        loading={loading}
        locale={{ emptyText: t('shell.inspector.empty') }}
        pagination={{
          current: query.page,
          pageSize: query.pageSize,
          total: load.page?.totalCount ?? 0,
          pageSizeOptions: pageSizes,
          showSizeChanger: true,
        }}
        onChange={onChange}
      />
    </Flex>
  )
}
