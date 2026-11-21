import { Flex } from 'antd'
import { StatusTag } from '../platform/StatusTag'
import { useContext, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router'
import { AxisAlert } from '../platform/AxisAlert'
import { AxisTable, type AxisColumn } from '../platform/AxisTable'
import {
  fetchStudioKindList,
  languageName,
  ServerUnreachableError,
  type StudioKindList as KindList,
  type StudioKindListItem,
} from '../platform/studio'
import { studioResourceAddress } from '../platform/studioAddress'
import { useStudioConnection } from '../platform/studioConnection'
import { recordPageSize, recordQuery, tablePageSizes } from '../platform/tableQuery'
import { useText } from '../platform/texts'
import { useTableView } from '../platform/useTableView'
import { PlatformContext } from './context'

/** The columns the view's search, filter and sort can name, see docs/reference/studio-resource-form.md#kind-list. */
const listFields = [
  { name: 'name', type: 'text' },
  { name: 'description', type: 'text' },
  { name: 'status', type: 'enum' },
  { name: 'modifiedBy', type: 'text' },
  { name: 'modifiedAt', type: 'date' },
] as const
// A kind's list has no parameters of its own. A stable empty array keeps useTableView from recomputing on each render.
const noParameters: readonly string[] = []

const statuses = ['new', 'changed', 'published'] as const

/**
 * A kind's list in Studio: one table of the application's own resources of the kind and the module resources of the
 * modules it pins, paged on the server, see docs/reference/studio.md#kind-lists-and-module-resources. A row shows the
 * name, a link that opens the resource, with a neutral tag of the module's label for a module resource, then the
 * description, the status and who changed it last and when. The table has Search, Filter, Columns, Refresh, a sort on Name,
 * Status, Last modified by and Last modified at, and a pager with a page size, all kept in the address, see
 * docs/reference/actions-and-controls.md#tables. The server builds the rows in the UI locale, see
 * docs/reference/studio-resource-form.md#kind-list. The list loads again after a write (`folderRevision`), a reconnect and
 * Refresh. A failed load shows one error above the table.
 */
export function StudioKindList({
  application,
  kind,
  folderRevision,
}: {
  application: string
  kind: string
  folderRevision: number
}) {
  const t = useText()
  const locale = useContext(PlatformContext)?.current.locale ?? 'en'
  const { generation, lost } = useStudioConnection()
  const { view, setView, saved } = useTableView(
    listFields,
    recordPageSize,
    noParameters,
    true,
    true,
    true,
    '',
    `studio.${application}.${kind}`,
  )
  const query = recordQuery(view)
  const [list, setList] = useState<KindList | 'failed'>()
  const [reload, setReload] = useState(0)
  const timeFormat = useMemo(
    () => new Intl.DateTimeFormat(locale, { dateStyle: 'medium', timeStyle: 'medium' }),
    [locale],
  )

  useEffect(() => {
    const controller = new AbortController()
    fetchStudioKindList(application, kind, locale, query, controller.signal).then(setList, (error: unknown) => {
      if (controller.signal.aborted) {
        return
      }
      if (error instanceof ServerUnreachableError) {
        lost()
        return
      }
      console.warn(`Loading the ${kind} list of '${application}' failed`, error)
      setList('failed')
    })
    return () => controller.abort()
  }, [application, kind, locale, query, folderRevision, generation, reload, lost])

  const columns: AxisColumn<StudioKindListItem>[] = [
    {
      key: 'name',
      kind: 'freeText',
      title: t('shell.studio.list.name'),
      filter: { kind: 'text' },
      sorter: true,
      render: (_value, row) => (
        <Flex gap="small" align="center" wrap>
          <Link data-testid={`studio-resource-${row.id}`} to={studioResourceAddress(application, row.id)}>
            {row.locale === null ? row.title : languageName(row.locale, locale)}
          </Link>
          {row.module !== null && <StatusTag tone="muted">{row.moduleLabel ?? row.module}</StatusTag>}
        </Flex>
      ),
    },
    {
      key: 'description',
      kind: 'freeText',
      title: t('shell.studio.list.description'),
      filter: { kind: 'text' },
      render: (_value, row) => row.description,
    },
    {
      key: 'status',
      kind: 'fixed',
      title: t('shell.studio.list.status'),
      filter: {
        kind: 'enum',
        options: statuses.map((status) => ({ value: status, label: t(`shell.studio.list.status.${status}`) })),
      },
      sorter: true,
      render: (_value, row) => (row.status === null ? null : t(`shell.studio.list.status.${row.status}`)),
    },
    {
      key: 'modifiedBy',
      kind: 'freeText',
      title: t('shell.studio.list.modifiedBy'),
      filter: { kind: 'text' },
      sorter: true,
      render: (_value, row) => row.modifiedBy,
    },
    {
      key: 'modifiedAt',
      kind: 'fixed',
      title: t('shell.studio.list.modifiedAt'),
      filter: { kind: 'date' },
      sorter: true,
      render: (_value, row) => (row.modifiedAt === null ? null : timeFormat.format(new Date(row.modifiedAt))),
    },
  ]
  const loaded = typeof list === 'object' ? list : undefined

  return (
    <AxisTable<StudioKindListItem>
      testId={`studio-group-${kind}`}
      columns={columns}
      rowKey={(row) => row.id}
      dataSource={loaded?.items ?? []}
      loading={list === undefined}
      view={{
        query: view,
        onChange: setView,
        saved,
        pageSizes: tablePageSizes(),
        total: loaded?.totalCount ?? 0,
        search: true,
        columns: true,
      }}
      toolbar={{ onRefresh: () => setReload((value) => value + 1) }}
      failure={
        list === 'failed' ? (
          <AxisAlert data-testid="studio-list-error" kind="error" message={t('shell.studio.list.loadFailed')} />
        ) : undefined
      }
    />
  )
}
