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
import { matchesFilter } from './tableFilter'
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
  /** The request URL, so a result for an older request shows as loading. */
  url?: string
  /** The last page loaded. It stays while the next page loads. */
  page?: InstancePage
  failed: boolean
  forbidden: boolean
}

const pageSizes = tablePageSizes()
// useTableView needs stable arrays. The list has no sort and no parameter of its own.
const stateFields = [{ name: 'state', type: 'enum' }] as const
const noParameters: readonly string[] = []
const createdAtField = { name: 'createdAt', type: 'date-time' } as const
const endedAtField = { name: 'endedAt', type: 'date-time' } as const

/**
 * Lists the instances of the widget's process, newest first, as the inspection API orders them. The table's Filter
 * filters them by State. `filter`, `page` and `pageSize` live in the URL, and a new filter goes back to page 1. The
 * browser works out which of the five states the filter keeps and sends each one as `state`, none when it keeps all of
 * them, and no request at all when it keeps none. The first column links each row to the instance's address
 * under the inspector page. A `403` shows that the user may not inspect the process. With nobody
 * signed in, the inspection API's 401 shows the sign-in prompt.
 *
 * The step column shows the step's name, because a list row carries no label key. Under the list, the
 * process's open dead letters have their own list, which has its own page number.
 */
export function ProcessInspectorWidget({ sitePath, pageName, widget, locale }: ProcessInspectorWidgetProps) {
  const t = useText()
  // The server sets the path on every process inspector.
  const instancesPath = widget.instancesPath!
  const instancePath = siteAddress(sitePath, pageName.toLowerCase())
  const { view: query, setView } = useTableView(stateFields, recordPageSize, noParameters, false, true)
  const { filter } = query
  const keptStates = useMemo(
    () =>
      filter
        ? instanceStates.filter((state) =>
            matchesFilter(
              { state },
              filter,
              () => ({ kind: 'enum', value: (row: { state: string }) => row.state }),
              locale,
            ),
          )
        : [...instanceStates],
    [filter, locale],
  )
  const noneKept = keptStates.length === 0
  // The filter itself stays in the address. The server gets the states it keeps, none when it keeps all of them.
  const requestQuery = useMemo(() => {
    const params = new URLSearchParams(recordQuery({ ...query, filter: undefined }))
    if (keptStates.length < instanceStates.length) {
      for (const state of keptStates) {
        params.append('state', state)
      }
    }
    return params.toString()
  }, [query, keptStates])
  const requestUrl = `${instancesPath}${requestQuery ? `?${requestQuery}` : ''}`
  const [load, setLoad] = useState<LoadState>({ failed: false, forbidden: false })
  const loading = !noneKept && load.url !== requestUrl
  const failed = load.failed && !loading && !noneKept
  const totalCount = noneKept ? 0 : loading ? undefined : load.page?.totalCount

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
    if (noneKept) {
      return
    }
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
  }, [instancesPath, requestQuery, requestUrl, noneKept])

  const columns = useMemo<AxisColumn<InstanceListItem>[]>(
    () => [
      {
        key: 'record',
        kind: 'freeText',
        title: t('shell.inspector.subject'),
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
      },
      { key: 'step', kind: 'freeText', title: t('shell.inspector.step'), render: (_value, instance) => instance.step },
      {
        key: 'release',
        kind: 'fixed',
        title: t('shell.inspector.release'),
        render: (_value, instance) => instance.releaseNumber ?? null,
      },
      {
        key: 'createdAt',
        kind: 'fixed',
        title: t('shell.inspector.createdAt'),
        render: (_value, instance) => formatValue(createdAtField, instance.createdAt, {}, { locale, text: t }),
      },
      {
        key: 'endedAt',
        kind: 'fixed',
        title: t('shell.inspector.endedAt'),
        render: (_value, instance) =>
          instance.endedAt === null ? null : formatValue(endedAtField, instance.endedAt, {}, { locale, text: t }),
      },
    ],
    [locale, t, instancePath],
  )

  if (load.forbidden && !loading) {
    return <AxisAlert data-testid="inspector-forbidden" kind="warning" message={t('shell.inspector.forbidden')} />
  }
  if (failed) {
    return <AxisAlert data-testid="inspector-error" kind="error" message={t('shell.inspector.loadFailed')} />
  }
  return (
    <Flex vertical gap="middle">
      <AxisTable<InstanceListItem>
        rowKey={(instance) => instance.id}
        columns={columns}
        dataSource={noneKept ? [] : load.page?.items}
        loading={loading}
        emptyText={t('shell.inspector.empty')}
        view={{ query, onChange: setView, total: noneKept ? 0 : (load.page?.totalCount ?? 0), pageSizes }}
      />
      <DeadLetterList deadLettersPath={widget.deadLettersPath!} locale={locale} />
    </Flex>
  )
}
